using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using ApplicationResourceRegistrationContext = FluxFlow.Composition.ApplicationResourceRegistrationContext;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationRuntimeConcurrencyTests
{
    private const int RequestBatchCount = 4;
    private const int RequestsPerBatch = 16;
    private const int RevisionAttemptCount = 8;
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Concurrent_requests_in_repeated_batches_preserve_every_value_and_trace_exactly_once()
    {
        var lifetime = new LifetimeTracker();
        var fixture = CreateRuntimeDefinition("reply:", lifetime);
        var services = new ServiceCollection();
        services.AddFluxFlow(fixture.Definition, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var assembler = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        (await application.StartAsync()).Status.ShouldBe(ApplicationUpdateStatus.Applied);
        var ports = application.Ports;
        var runtime = assembler.GetRequiredPorts();
        var rejections = new BufferBlock<ApplicationPortRejection>();
        using var rejectionLink = runtime.Rejections.LinkTo(
            rejections,
            new DataflowLinkOptions { PropagateCompletion = true });
        var evidence = new List<RequestEvidence>(RequestBatchCount * RequestsPerBatch);

        for (var batch = 0; batch < RequestBatchCount; batch++)
        {
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = Enumerable.Range(0, RequestsPerBatch)
                .Select(async index =>
                {
                    await release.Task;
                    var request = FlowMessage.Create($"batch-{batch}:request-{index}");
                    var operation = ports.SendAndReceiveAsync(
                        fixture.Node.Input,
                        fixture.Node.Output,
                        request);
                    var result = await operation.WaitAsync(DeadlockGuard);
                    return new RequestEvidence(request, result);
                })
                .ToArray();

            release.TrySetResult(true).ShouldBeTrue();
            evidence.AddRange(await Task.WhenAll(requests));
        }

        evidence.Count.ShouldBe(RequestBatchCount * RequestsPerBatch);
        evidence.Select(static item => item.Request.Value)
            .Distinct(StringComparer.Ordinal).Count().ShouldBe(evidence.Count);
        evidence.Select(static item => item.Request.TraceId)
            .Distinct().Count().ShouldBe(evidence.Count);
        foreach (var item in evidence)
        {
            item.Result.Status.ShouldBe(PortRequestStatus.Received);
            item.Result.InputPort.ShouldBe(fixture.Node.Input.Address);
            item.Result.OutputPort.ShouldBe(fixture.Node.Output.Address);
            item.Result.Response.ShouldNotBeNull();
            item.Result.Response.TraceId.ShouldBe(item.Request.TraceId);
            item.Result.Response.Value.ShouldBe($"reply:{item.Request.Value}");
        }

        runtime.ShouldBeSameAs(assembler.GetRequiredPorts());
        application.Ports.ShouldBeSameAs(ports);
        runtime.Status.Ports.ShouldAllBe(static status => status.PendingMessages == 0);

        await application.StopAsync();
        await rejections.Completion.WaitAsync(DeadlockGuard);
        Drain(rejections).ShouldBeEmpty();
        lifetime.Created.ShouldBe(1);
        lifetime.Disposed.ShouldBe(1);
    }

    [Fact]
    public async Task Repeated_compatible_reloads_preserve_stable_ports_exact_routing_and_single_retirement()
    {
        var lifetimes = new List<LifetimeTracker>();
        var initialLifetime = new LifetimeTracker();
        lifetimes.Add(initialLifetime);
        var currentFixture = CreateRuntimeDefinition("revision-0:", initialLifetime);
        var services = new ServiceCollection();
        services.AddFluxFlow(currentFixture.Definition, options => options.StartWithHost = false);
        var provider = services.BuildServiceProvider();

        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            var assembler = provider.GetRequiredService<ApplicationRuntimeAssembler>();
            var started = await application.StartAsync();
            started.Status.ShouldBe(ApplicationUpdateStatus.Applied);
            var stablePorts = application.Ports;
            var stableRuntime = assembler.GetRequiredPorts();
            var stableInput = currentFixture.Node.Input;
            var stableOutput = currentFixture.Node.Output;

            await AssertRoutedBatchAsync(
                stablePorts,
                stableInput,
                stableOutput,
                revision: 0,
                expectedPrefix: "revision-0:");

            for (var attempt = 1; attempt <= RevisionAttemptCount; attempt++)
            {
                var previousFixture = currentFixture;
                var currentLifetime = new LifetimeTracker();
                lifetimes.Add(currentLifetime);
                currentFixture = CreateRuntimeDefinition($"revision-{attempt}:", currentLifetime);
                currentFixture.Contract.Descriptor.ShouldNotBeSameAs(previousFixture.Contract.Descriptor);
                var previousSnapshot = application.Current;

                var applied = await application.ApplyAsync(
                    $"compatible-{attempt}",
                    currentFixture.Definition);

                applied.Status.ShouldBe(ApplicationUpdateStatus.Applied);
                applied.RequestedRevisionId.ShouldBe($"compatible-{attempt}");
                applied.PreviousRevision.ShouldBeSameAs(previousSnapshot);
                applied.ActiveRevision.ShouldBeSameAs(application.Current);
                application.CurrentDefinition.ShouldBeSameAs(currentFixture.Definition);
                application.Current!.Sequence.ShouldBe(attempt + 1L);
                application.Current.RevisionId.ShouldBe($"compatible-{attempt}");
                application.Ports.ShouldBeSameAs(stablePorts);
                assembler.GetRequiredPorts().ShouldBeSameAs(stableRuntime);
                stableRuntime.CurrentRevision!.RevisionId.ShouldBe($"compatible-{attempt}");
                stableRuntime.CurrentRevision.Sequence.ShouldBe(attempt + 1L);
                lifetimes.Take(attempt).ShouldAllBe(static tracker => tracker.Disposed == 1);
                currentLifetime.Created.ShouldBe(1);
                currentLifetime.Disposed.ShouldBe(0);

                await AssertRoutedBatchAsync(
                    stablePorts,
                    stableInput,
                    stableOutput,
                    attempt,
                    $"revision-{attempt}:");
            }

            await application.StopAsync();
            await application.StopAsync();
            stableRuntime.Status.State.ShouldBe(ApplicationRuntimeState.Disposed);
        }
        finally
        {
            await provider.DisposeAsync();
        }

        lifetimes.Count.ShouldBe(RevisionAttemptCount + 1);
        lifetimes.ShouldAllBe(static tracker => tracker.Created == 1);
        lifetimes.ShouldAllBe(static tracker => tracker.Disposed == 1);
    }

    [Fact]
    public async Task Repeated_rejected_reloads_preserve_active_route_and_dispose_each_candidate_once()
    {
        var activeLifetime = new LifetimeTracker();
        var activeResourceLifetime = new LifetimeTracker();
        var activeGuardAttempts = new FactoryAttemptTracker();
        var active = CreateGuardedDefinition(
            "active:",
            activeLifetime,
            activeResourceLifetime,
            activeGuardAttempts,
            failGuard: false);
        var services = new ServiceCollection();
        services.AddFluxFlow(active.Definition, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var assembler = provider.GetRequiredService<ApplicationRuntimeAssembler>();

        (await application.StartAsync()).Status.ShouldBe(ApplicationUpdateStatus.Applied);
        var activeSnapshot = application.Current;
        var activeDefinition = application.CurrentDefinition;
        var activePorts = application.Ports;
        var activeRuntime = assembler.GetRequiredPorts();
        var candidateLifetimes = new List<LifetimeTracker>();
        var candidateResourceLifetimes = new List<LifetimeTracker>();
        activeLifetime.Created.ShouldBe(2);
        activeLifetime.Disposed.ShouldBe(0);
        activeResourceLifetime.Created.ShouldBe(1);
        activeResourceLifetime.Disposed.ShouldBe(0);
        activeGuardAttempts.Attempts.ShouldBe(1);

        for (var attempt = 1; attempt <= RevisionAttemptCount; attempt++)
        {
            await AssertSingleRouteAsync(
                activePorts,
                active.Route.Input,
                active.Route.Output,
                $"before-{attempt}",
                "active:");
            var candidateLifetime = new LifetimeTracker();
            candidateLifetimes.Add(candidateLifetime);
            var candidateResourceLifetime = new LifetimeTracker();
            candidateResourceLifetimes.Add(candidateResourceLifetime);
            var candidateGuardAttempts = new FactoryAttemptTracker();
            var candidate = CreateGuardedDefinition(
                $"candidate-{attempt}:",
                candidateLifetime,
                candidateResourceLifetime,
                candidateGuardAttempts,
                failGuard: true);
            candidate.RouteContract.Descriptor.ShouldNotBeSameAs(active.RouteContract.Descriptor);

            var rejected = await application.ApplyAsync(
                $"rejected-{attempt}",
                candidate.Definition);

            rejected.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
            rejected.RequestedRevisionId.ShouldBe($"rejected-{attempt}");
            rejected.ActiveRevision.ShouldBeSameAs(activeSnapshot);
            rejected.PreviousRevision.ShouldBeSameAs(activeSnapshot);
            var diagnostic = rejected.Diagnostics.ShouldHaveSingleItem();
            diagnostic.Stage.ShouldBe(ApplicationUpdateStage.ComponentPreparation);
            diagnostic.Error.Code.ShouldBe("revision.preparation.failed");
            diagnostic.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!
                .ShouldContain($"candidate-{attempt} activation failed");
            application.State.ShouldBe(ApplicationState.Running);
            application.Current.ShouldBeSameAs(activeSnapshot);
            application.CurrentDefinition.ShouldBeSameAs(activeDefinition);
            application.Ports.ShouldBeSameAs(activePorts);
            assembler.GetRequiredPorts().ShouldBeSameAs(activeRuntime);
            activeRuntime.CurrentRevision!.RevisionId.ShouldBe("initial");
            activeRuntime.CurrentRevision.Sequence.ShouldBe(1);
            activeLifetime.Disposed.ShouldBe(0);
            activeResourceLifetime.Disposed.ShouldBe(0);
            candidateGuardAttempts.Attempts.ShouldBe(1);
            candidateLifetime.Created.ShouldBeLessThanOrEqualTo(1);
            candidateLifetime.Disposed.ShouldBe(candidateLifetime.Created);
            candidateLifetimes.ShouldAllBe(static tracker => tracker.Disposed == tracker.Created);
            candidateResourceLifetime.Created.ShouldBe(1);
            candidateResourceLifetime.Disposed.ShouldBe(1);
            candidateResourceLifetimes.ShouldAllBe(static tracker =>
                tracker.Created == 1 && tracker.Disposed == 1);

            await AssertSingleRouteAsync(
                activePorts,
                active.Route.Input,
                active.Route.Output,
                $"after-{attempt}",
                "active:");
        }

        await application.StopAsync();
        await application.StopAsync();
        activeLifetime.Disposed.ShouldBe(2);
        activeResourceLifetime.Disposed.ShouldBe(1);
        candidateLifetimes.Count.ShouldBe(RevisionAttemptCount);
        candidateLifetimes.ShouldAllBe(static tracker => tracker.Disposed == tracker.Created);
        candidateResourceLifetimes.Count.ShouldBe(RevisionAttemptCount);
        candidateResourceLifetimes.ShouldAllBe(static tracker =>
            tracker.Created == 1 && tracker.Disposed == 1);
    }

    [Fact]
    public async Task Stop_during_causally_blocked_processing_drains_every_accepted_message_and_disposes_once()
    {
        var gate = new ProcessingGate();
        var componentLifetime = new LifetimeTracker();
        var resourceLifetime = new LifetimeTracker();
        var fixture = CreateShutdownDefinition(gate, componentLifetime, resourceLifetime);
        var services = new ServiceCollection();
        services.AddFluxFlow(fixture.Definition, options => options.StartWithHost = false);
        var provider = services.BuildServiceProvider();
        Task? stop = null;

        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            var assembler = provider.GetRequiredService<ApplicationRuntimeAssembler>();
            (await application.StartAsync()).Status.ShouldBe(ApplicationUpdateStatus.Applied);
            var activeRuntime = assembler.GetRequiredPorts();
            var observed = await application.Ports.ObserveAsync(fixture.Node.Output, capacity: 8);
            observed.Status.ShouldBe(PortObserveStatus.Started);
            await using var observation = observed.Observation!;
            var values = new[] { "first", "second", "third", "fourth" };
            var collect = ReadExactlyAsync(observation, values.Length);

            foreach (var value in values)
            {
                var sent = await application.Ports.SendAsync(
                    fixture.Node.Input,
                    FlowMessage.Create(value));
                sent.Status.ShouldBe(PortSendStatus.Accepted);
            }

            await gate.Entered.WaitAsync(DeadlockGuard);
            stop = application.StopAsync().AsTask();
            application.State.ShouldBe(ApplicationState.Stopping);
            stop.IsCompleted.ShouldBeFalse();
            componentLifetime.Disposed.ShouldBe(0);
            resourceLifetime.Disposed.ShouldBe(0);

            gate.Release();
            await stop.WaitAsync(DeadlockGuard);
            var drained = await collect.WaitAsync(DeadlockGuard);

            drained.Select(static message => message.Value).ShouldBe(
                values.Select(static value => $"drained:{value}"));
            application.State.ShouldBe(ApplicationState.Stopped);
            application.Current.ShouldBeNull();
            activeRuntime.Status.State.ShouldBe(ApplicationRuntimeState.Disposed);
            activeRuntime.Status.Ports.ShouldAllBe(static status =>
                status.Availability == ApplicationPortAvailability.Completed &&
                status.PendingMessages == 0);
            (await activeRuntime.SendAsync(
                    fixture.Node.Input.Address,
                    FlowMessage.Create("late")))
                .Status.ShouldBe(PortSendStatus.Completed);
            componentLifetime.Created.ShouldBe(1);
            componentLifetime.Disposed.ShouldBe(1);
            resourceLifetime.Created.ShouldBe(1);
            resourceLifetime.Disposed.ShouldBe(1);

            await application.StopAsync();
            componentLifetime.Disposed.ShouldBe(1);
            resourceLifetime.Disposed.ShouldBe(1);
        }
        finally
        {
            gate.Release();
            if (stop is not null)
                await stop.WaitAsync(DeadlockGuard);
            await provider.DisposeAsync();
        }

        componentLifetime.Disposed.ShouldBe(1);
        resourceLifetime.Disposed.ShouldBe(1);
    }

    private static RuntimeDefinitionFixture CreateRuntimeDefinition(
        string prefix,
        LifetimeTracker lifetime)
    {
        var contract = CreatePrefixContract("test.hardening.runtime", lifetime);
        var builder = new ApplicationDefinitionBuilder();
        var node = builder.AddWorkflow("Main").AddComponent(
            "Value",
            contract,
            options => options.Prefix = prefix);
        return new RuntimeDefinitionFixture(builder.Build(), node, contract);
    }

    private static GuardedDefinitionFixture CreateGuardedDefinition(
        string prefix,
        LifetimeTracker lifetime,
        LifetimeTracker resourceLifetime,
        FactoryAttemptTracker guardAttempts,
        bool failGuard)
    {
        const string resourceName = "CandidateOwnership";
        var resourceContract = CreateOwnershipResourceContract(
            "test.hardening.rejection-resource",
            resourceName,
            resourceLifetime);
        var routeContract = CreatePrefixContract("test.hardening.route", lifetime);
        var guardContract = ComponentContract.Create<GuardOptions, InputOutputComponentHandle<string, string>>(
            "test.hardening.guard",
            component =>
            {
                component.AddResource<OwnershipResource>("Ownership", isRequired: true);
                component
                    .UseFactory(context =>
                    {
                        _ = context.GetRequiredResource<OwnershipResource>("Ownership");
                        guardAttempts.Record();
                        if (failGuard)
                        {
                            throw new InvalidOperationException(
                                $"{context.GetConfigurationValue<string>("Prefix")} activation failed");
                        }

                        return new TrackedPrefixNode(
                            context.GetConfigurationValue<string>("Prefix")!,
                            lifetime);
                    })
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events);
            },
            static () => new GuardOptions(),
            static (options, definition) =>
            {
                definition.Set("Prefix", options.Prefix);
                definition.UseResource("Ownership", options.Resource!.Definition);
            },
            CreateInputOutputHandle);
        var builder = new ApplicationDefinitionBuilder();
        var resource = builder.AddResource(resourceName, resourceContract);
        var workflow = builder.AddWorkflow("Main");
        var route = workflow.AddComponent(
            "AValue",
            routeContract,
            options => options.Prefix = prefix);
        workflow.AddComponent(
            "ZGuard",
            guardContract,
            options =>
            {
                options.Prefix = failGuard ? prefix.TrimEnd(':') : "active-guard";
                options.Resource = resource;
            });
        return new GuardedDefinitionFixture(builder.Build(), route, routeContract);
    }

    private static ComponentContract<PrefixOptions, InputOutputComponentHandle<string, string>>
        CreatePrefixContract(string type, LifetimeTracker lifetime)
        => ComponentContract.Create<PrefixOptions, InputOutputComponentHandle<string, string>>(
            type,
            component => component
                .UseFactory(context => new TrackedPrefixNode(
                    context.GetConfigurationValue<string>("Prefix")!,
                    lifetime))
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events),
            static () => new PrefixOptions(),
            static (options, definition) => definition.Set("Prefix", options.Prefix),
            CreateInputOutputHandle);

    private static InputOutputComponentHandle<string, string> CreateInputOutputHandle(
        ComponentHandle component)
        => new(component, "Input", "Output", "Events");

    private static ShutdownDefinitionFixture CreateShutdownDefinition(
        ProcessingGate gate,
        LifetimeTracker componentLifetime,
        LifetimeTracker resourceLifetime)
    {
        var resourceContract = CreateOwnershipResourceContract(
            "test.hardening.ownership",
            "Ownership",
            resourceLifetime);
        var componentContract = ComponentContract.Create<OwnershipComponentOptions,
            InputOutputComponentHandle<string, string>>(
            "test.hardening.blocking",
            component =>
            {
                component.AddResource<OwnershipResource>("Ownership", isRequired: true);
                component
                    .UseFactory(context =>
                    {
                        _ = context.GetRequiredResource<OwnershipResource>("Ownership");
                        return new BlockingDrainNode(gate, componentLifetime);
                    })
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events);
            },
            static () => new OwnershipComponentOptions(),
            static (options, definition) =>
                definition.UseResource("Ownership", options.Resource!.Definition),
            CreateInputOutputHandle);
        var builder = new ApplicationDefinitionBuilder();
        var resource = builder.AddResource("Ownership", resourceContract);
        var node = builder.AddWorkflow("Main").AddComponent(
            "Value",
            componentContract,
            options => options.Resource = resource);
        return new ShutdownDefinitionFixture(builder.Build(), node);
    }

    private static ApplicationResourceContract<OwnershipResourceHandle>
        CreateOwnershipResourceContract(
            string type,
            string resourceName,
            LifetimeTracker lifetime)
        => ApplicationResourceContract.Create<OwnershipResourceHandle>(
            type,
            new OwnershipResourceRegistrar(resourceName, lifetime),
            static resource => new OwnershipResourceHandle(resource));

    private static async Task AssertRoutedBatchAsync(
        ApplicationPorts ports,
        InputPortHandle<string> input,
        OutputPortHandle<string> output,
        int revision,
        string expectedPrefix)
    {
        for (var index = 0; index < 4; index++)
        {
            await AssertSingleRouteAsync(
                ports,
                input,
                output,
                $"revision-{revision}:value-{index}",
                expectedPrefix);
        }
    }

    private static async Task AssertSingleRouteAsync(
        ApplicationPorts ports,
        InputPortHandle<string> input,
        OutputPortHandle<string> output,
        string value,
        string expectedPrefix)
    {
        var request = FlowMessage.Create(value);
        var result = await ports.SendAndReceiveAsync(input, output, request)
            .WaitAsync(DeadlockGuard);

        result.Status.ShouldBe(PortRequestStatus.Received);
        result.Response.ShouldNotBeNull();
        result.Response.TraceId.ShouldBe(request.TraceId);
        result.Response.Value.ShouldBe($"{expectedPrefix}{value}");
    }

    private static async Task<IReadOnlyList<FlowMessage<string>>> ReadExactlyAsync(
        PortObservation<string> observation,
        int count)
    {
        var messages = new List<FlowMessage<string>>(count);
        while (messages.Count < count)
        {
            messages.Add(await observation.Messages.ReceiveAsync()
                .WaitAsync(DeadlockGuard));
        }

        return messages;
    }

    private static IReadOnlyList<T> Drain<T>(BufferBlock<T> source)
    {
        var values = new List<T>();
        while (source.TryReceive(out var value))
            values.Add(value);
        return values;
    }

    private sealed record RequestEvidence(
        FlowMessage<string> Request,
        PortRequestResult<string> Result);

    private sealed record RuntimeDefinitionFixture(
        ApplicationDefinition Definition,
        InputOutputComponentHandle<string, string> Node,
        ComponentContract<PrefixOptions, InputOutputComponentHandle<string, string>> Contract);

    private sealed record GuardedDefinitionFixture(
        ApplicationDefinition Definition,
        InputOutputComponentHandle<string, string> Route,
        ComponentContract<PrefixOptions, InputOutputComponentHandle<string, string>> RouteContract);

    private sealed record ShutdownDefinitionFixture(
        ApplicationDefinition Definition,
        InputOutputComponentHandle<string, string> Node);

    private sealed class PrefixOptions
    {
        public string Prefix { get; set; } = string.Empty;
    }

    private sealed class OwnershipComponentOptions
    {
        public OwnershipResourceHandle? Resource { get; set; }
    }

    private sealed class GuardOptions
    {
        public string Prefix { get; set; } = string.Empty;

        public OwnershipResourceHandle? Resource { get; set; }
    }

    private sealed class TrackedPrefixNode : FlowNode<string, string>
    {
        private readonly string _prefix;
        private readonly LifetimeTracker _lifetime;

        public TrackedPrefixNode(string prefix, LifetimeTracker lifetime)
        {
            _prefix = prefix;
            _lifetime = lifetime;
            _lifetime.RecordCreated();
        }

        protected override async Task ProcessAsync(FlowMessage<string> message)
            => await EmitAsync(message.With($"{_prefix}{message.Value}"), Stopping)
                .ConfigureAwait(false);

        protected override ValueTask OnDisposeAsync()
        {
            _lifetime.RecordDisposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDrainNode : FlowNode<string, string>
    {
        private readonly ProcessingGate _gate;
        private readonly LifetimeTracker _lifetime;
        private int _processed;

        public BlockingDrainNode(ProcessingGate gate, LifetimeTracker lifetime)
        {
            _gate = gate;
            _lifetime = lifetime;
            _lifetime.RecordCreated();
        }

        protected override async Task ProcessAsync(FlowMessage<string> message)
        {
            if (Interlocked.Increment(ref _processed) == 1)
            {
                _gate.MarkEntered();
                await _gate.Released.ConfigureAwait(false);
            }

            await EmitAsync(message.With($"drained:{message.Value}"), Stopping)
                .ConfigureAwait(false);
        }

        protected override ValueTask OnDisposeAsync()
        {
            _lifetime.RecordDisposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProcessingGate
    {
        private readonly TaskCompletionSource<bool> _entered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public Task Released => _released.Task;

        public void MarkEntered() => _entered.TrySetResult(true);

        public void Release() => _released.TrySetResult(true);
    }

    private sealed class LifetimeTracker
    {
        private int _created;
        private int _disposed;

        public int Created => Volatile.Read(ref _created);

        public int Disposed => Volatile.Read(ref _disposed);

        public void RecordCreated() => Interlocked.Increment(ref _created);

        public void RecordDisposed() => Interlocked.Increment(ref _disposed);
    }

    private sealed class FactoryAttemptTracker
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public void Record() => Interlocked.Increment(ref _attempts);
    }

    private sealed class OwnershipResourceHandle(ResourceHandle resource)
        : AuthoredResourceHandle(resource);

    private sealed class OwnershipResource : IAsyncDisposable
    {
        private readonly LifetimeTracker _lifetime;

        public OwnershipResource(LifetimeTracker lifetime)
        {
            _lifetime = lifetime;
            _lifetime.RecordCreated();
        }

        public ValueTask DisposeAsync()
        {
            _lifetime.RecordDisposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class OwnershipResourceRegistrar(
        string resourceName,
        LifetimeTracker lifetime)
        : IApplicationResourceRegistrar
    {
        public void Register(ApplicationResourceRegistrationContext context)
            => context.Services.AddFluxFlowResource<OwnershipResource>(
                ApplicationAddress.Resource(resourceName),
                _ => new OwnershipResource(lifetime));
    }
}
