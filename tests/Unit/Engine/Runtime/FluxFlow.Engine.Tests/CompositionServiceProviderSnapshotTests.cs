using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine.Internal.Snapshots;
using FluxFlow.Composition.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class CompositionServiceProviderSnapshotTests
{
    private static readonly ApplicationAddress Component =
        ApplicationAddress.WorkflowComponent("Orders", "Normalize");
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("Orders", "Normalize", "Input");
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Orders", "Normalize", "Output");
    private static readonly ApplicationAddress Signal =
        ApplicationAddress.WorkflowPort("Orders", "Normalize", "Ack");
    private static readonly ApplicationAddress Resource =
        ApplicationAddress.Resource("Messaging", "Client1");

    [Fact]
    public void Builder_copies_service_collections_and_composes_them_in_order()
    {
        var first = new ServiceCollection();
        first.AddSingleton<IValueService>(new ValueService("first"));
        var second = new ServiceCollection();
        second.AddSingleton<IValueService>(new ValueService("second"));
        var builder = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(first)
            .AddServices(second);
        first.AddSingleton<LateService>();

        using var snapshot = builder.Build(
            CompositionProviderBoundary.ResourceRevision,
            "resources-1");

        snapshot.GetRequiredService<IValueService>().Value.ShouldBe("second");
        snapshot.GetService<LateService>().ShouldBeNull();
        snapshot.Boundary.ShouldBe(CompositionProviderBoundary.ResourceRevision);
        snapshot.Name.ShouldBe("resources-1");
        snapshot.OwnsProvider.ShouldBeTrue();
        snapshot.ServiceCount.ShouldBe(2);
        builder.ServiceCount.ShouldBe(2);
    }

    [Fact]
    public void Builder_uses_validation_safe_defaults_and_rejects_invalid_boundaries()
    {
        var services = new ServiceCollection();
        services.AddSingleton<InvalidService>();
        var builder = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services);

        Should.Throw<AggregateException>(() => builder.Build(
            CompositionProviderBoundary.WorkflowRevision,
            "workflow-invalid"));
        Should.Throw<ArgumentOutOfRangeException>(() => builder.Build(
            (CompositionProviderBoundary)99,
            "unknown"));
    }

    [Fact]
    public void Snapshot_scopes_are_available_but_not_implicit()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedService>();
        using var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.WorkflowRevision, "workflow-1");

        Should.Throw<InvalidOperationException>(() =>
            snapshot.GetRequiredService<ScopedService>());
        using var first = snapshot.Services.CreateScope();
        using var second = snapshot.Services.CreateScope();

        first.ServiceProvider.GetRequiredService<ScopedService>()
            .ShouldBeSameAs(first.ServiceProvider.GetRequiredService<ScopedService>());
        first.ServiceProvider.GetRequiredService<ScopedService>()
            .ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<ScopedService>());
    }

    [Fact]
    public async Task Owned_services_are_disposed_once_while_explicit_bridges_remain_external()
    {
        var external = new TrackedService();
        var services = new ServiceCollection();
        services.AddSingleton<TrackedService>();
        var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .BridgeExternalSingleton<IExternalService>(external)
            .BridgeExternalResource(Resource, external)
            .Build(CompositionProviderBoundary.ResourceRevision, "resources-2");
        var owned = snapshot.GetRequiredService<TrackedService>();

        await snapshot.DisposeAsync();

        owned.DisposeCount.ShouldBe(1);
        external.DisposeCount.ShouldBe(0);
        Should.Throw<ObjectDisposedException>(() => snapshot.GetService(typeof(TrackedService)));
    }

    [Fact]
    public async Task Snapshot_primary_services_override_host_fallback_for_unkeyed_and_keyed_resolution()
    {
        var hostUnkeyed = new TrackedService();
        var hostKeyed = new TrackedService();
        var hostServices = new ServiceCollection();
        hostServices.AddSingleton<IExternalService>(hostUnkeyed);
        hostServices.AddKeyedSingleton<IExternalService>("shared", hostKeyed);
        await using var hostProvider = hostServices.BuildServiceProvider();
        var revisionUnkeyed = new TrackedService();
        var revisionKeyed = new TrackedService();
        var revisionServices = new ServiceCollection();
        revisionServices.AddSingleton<IExternalService>(_ => revisionUnkeyed);
        revisionServices.AddKeyedSingleton<IExternalService>(
            "shared",
            (_, _) => revisionKeyed);
        var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(revisionServices)
            .Build(
                CompositionProviderBoundary.ResourceRevision,
                "resources-primary",
                fallbackProvider: hostProvider);

        snapshot.GetRequiredService<IExternalService>().ShouldBeSameAs(revisionUnkeyed);
        snapshot.GetRequiredKeyedService<IExternalService>("shared")
            .ShouldBeSameAs(revisionKeyed);

        await snapshot.DisposeAsync();

        revisionUnkeyed.DisposeCount.ShouldBe(1);
        revisionKeyed.DisposeCount.ShouldBe(1);
        hostUnkeyed.DisposeCount.ShouldBe(0);
        hostKeyed.DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Snapshot_resolves_unkeyed_and_keyed_host_fallback_without_taking_ownership()
    {
        var hostUnkeyed = new TrackedService();
        var hostKeyed = new TrackedService();
        var hostServices = new ServiceCollection();
        hostServices.AddSingleton<IExternalService>(hostUnkeyed);
        hostServices.AddKeyedSingleton<IExternalService>("host-only", hostKeyed);
        await using var hostProvider = hostServices.BuildServiceProvider();
        var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .Build(
                CompositionProviderBoundary.ResourceRevision,
                "resources-fallback",
                fallbackProvider: hostProvider);

        snapshot.GetRequiredService<IExternalService>().ShouldBeSameAs(hostUnkeyed);
        snapshot.GetRequiredKeyedService<IExternalService>("host-only")
            .ShouldBeSameAs(hostKeyed);

        await snapshot.DisposeAsync();

        hostUnkeyed.DisposeCount.ShouldBe(0);
        hostKeyed.DisposeCount.ShouldBe(0);
        hostProvider.GetRequiredService<IExternalService>().ShouldBeSameAs(hostUnkeyed);
        hostProvider.GetRequiredKeyedService<IExternalService>("host-only")
            .ShouldBeSameAs(hostKeyed);
    }

    [Fact]
    public async Task External_host_snapshot_never_takes_provider_ownership()
    {
        var tracked = new TrackedService();
        var services = new ServiceCollection();
        services.AddSingleton<IExternalService>(_ => tracked);
        await using var provider = services.BuildServiceProvider();
        var snapshot = CompositionServiceProviderSnapshot.CreateExternalHost(
            "host",
            provider);

        snapshot.GetRequiredService<IExternalService>().ShouldBeSameAs(tracked);
        snapshot.Boundary.ShouldBe(CompositionProviderBoundary.Host);
        snapshot.OwnsProvider.ShouldBeFalse();
        snapshot.ServiceCount.ShouldBeNull();
        await snapshot.DisposeAsync();

        tracked.DisposeCount.ShouldBe(0);
        provider.GetRequiredService<IExternalService>().ShouldBeSameAs(tracked);
    }

    [Fact]
    public async Task Snapshot_resolution_is_stable_under_concurrency()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValueService>();
        await using var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.Host, "owned-host");

        var resolved = new ConcurrentBag<ValueService>();
        await Task.WhenAll(Enumerable.Range(0, 128).Select(_ => Task.Run(() =>
            resolved.Add(snapshot.GetRequiredService<ValueService>()))));

        resolved.Count.ShouldBe(128);
        resolved.ShouldAllBe(service => ReferenceEquals(service, resolved.First()));
    }

    [Fact]
    public void Snapshot_info_json_contract_is_stable()
    {
        var info = new CompositionProviderSnapshotInfo
        {
            Name = "workflow-7",
            Boundary = CompositionProviderBoundary.WorkflowRevision,
            CreatedAt = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero),
            OwnsProvider = true,
            ServiceCount = 12
        };

        JsonSerializer.Serialize(info).ShouldBe(
            "{\"Name\":\"workflow-7\",\"Boundary\":3," +
            "\"CreatedAt\":\"2026-07-17T01:02:03+00:00\"," +
            "\"OwnsProvider\":true,\"ServiceCount\":12}");
    }

    [Fact]
    public void Canonical_resource_keys_resolve_through_factory_context()
    {
        var value = new ValueService("client-1");
        var services = new ServiceCollection();
        services.AddExternalFluxFlowResource<IValueService>(Resource, value);
        using var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.ResourceRevision, "resources-3");
        var context = new ComponentActivationContext(
            snapshot,
            "Orders",
            "Publish",
            new ComponentDefinition(
                "test",
                [new KeyValuePair<string, JsonElement>(
                    "client",
                    JsonSerializer.SerializeToElement(Resource.Value))]));

        snapshot.GetRequiredKeyedService<IValueService>(Resource.Value)
            .ShouldBeSameAs(value);
        context.GetRequiredResource<IValueService>("client").ShouldBeSameAs(value);
    }

    [Fact]
    public async Task Component_and_typed_port_views_forward_without_duplicate_ownership()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowComponent(Component, _ => new TestComponent());
        services.AddFluxFlowInputPortView<string>(Input, provider =>
            provider.GetRequiredKeyedService<TestComponent>(Component.Value).Input);
        services.AddFluxFlowOutputPortView<string>(Output, provider =>
            provider.GetRequiredKeyedService<TestComponent>(Component.Value).Output);
        var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.WorkflowRevision, "workflow-2");
        var component = snapshot.GetRequiredKeyedService<TestComponent>(Component.Value);
        var block = snapshot.GetRequiredKeyedService<IDataflowBlock>(Component.Value);
        var input = snapshot.GetRequiredKeyedService<ITargetBlock<FlowMessage<string>>>(Input.Value);
        var output = snapshot.GetRequiredKeyedService<ISourceBlock<FlowMessage<string>>>(Output.Value);

        (await input.SendAsync(FlowMessage.Create("hello"))).ShouldBeTrue();
        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .Value.ShouldBe("HELLO");
        block.ShouldNotBeSameAs(component);
        block.Completion.ShouldBeSameAs(component.Completion);
        block.Complete();
        await component.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await snapshot.DisposeAsync();

        component.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task External_component_registration_does_not_transfer_disposal_ownership()
    {
        var component = new TestComponent();
        var services = new ServiceCollection();
        services.AddExternalFluxFlowComponent(Component, component);
        var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.WorkflowRevision, "workflow-3");

        snapshot.GetRequiredKeyedService<TestComponent>(Component.Value)
            .ShouldBeSameAs(component);
        await snapshot.DisposeAsync();

        component.DisposeCount.ShouldBe(0);
        await component.DisposeAsync();
    }

    [Fact]
    public async Task Factory_created_ports_and_signal_targets_are_provider_owned()
    {
        var input = new DisposableTargetBlock<FlowMessage<string>>();
        var output = new DisposableSourceBlock<FlowMessage<string>>();
        var signal = new DisposableSignalTarget();
        var services = new ServiceCollection();
        services.AddFluxFlowInputPort<string>(Input, _ => input);
        services.AddFluxFlowOutputPort<string>(Output, _ => output);
        services.AddFluxFlowSignalTarget(Signal, _ => signal);
        var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.WorkflowRevision, "workflow-owned-ports");

        snapshot.GetRequiredKeyedService<ITargetBlock<FlowMessage<string>>>(Input.Value)
            .ShouldBeSameAs(input);
        snapshot.GetRequiredKeyedService<ISourceBlock<FlowMessage<string>>>(Output.Value)
            .ShouldBeSameAs(output);
        snapshot.GetRequiredKeyedService<IFlowSignalTarget>(Signal.Value)
            .ShouldBeSameAs(signal);

        await snapshot.DisposeAsync();

        input.DisposeCount.ShouldBe(1);
        output.DisposeCount.ShouldBe(1);
        signal.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task External_ports_and_signal_targets_remain_externally_owned()
    {
        var input = new DisposableTargetBlock<FlowMessage<string>>();
        var output = new DisposableSourceBlock<FlowMessage<string>>();
        var signal = new DisposableSignalTarget();
        var services = new ServiceCollection();
        services.AddExternalFluxFlowInputPort(Input, input);
        services.AddExternalFluxFlowOutputPort(Output, output);
        services.AddExternalFluxFlowSignalTarget(Signal, signal);
        var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.WorkflowRevision, "workflow-external-ports");

        snapshot.GetRequiredKeyedService<ITargetBlock<FlowMessage<string>>>(Input.Value)
            .ShouldNotBeSameAs(input);
        snapshot.GetRequiredKeyedService<ISourceBlock<FlowMessage<string>>>(Output.Value)
            .ShouldNotBeSameAs(output);
        snapshot.GetRequiredKeyedService<IFlowSignalTarget>(Signal.Value)
            .ShouldNotBeSameAs(signal);

        await snapshot.DisposeAsync();

        input.DisposeCount.ShouldBe(0);
        output.DisposeCount.ShouldBe(0);
        signal.DisposeCount.ShouldBe(0);
    }

    [Fact]
    public async Task Signal_target_is_payload_agnostic_and_preserves_message_identity()
    {
        var target = new RecordingSignalTarget();
        var services = new ServiceCollection();
        services.AddExternalFluxFlowSignalTarget(Signal, target);
        await using var snapshot = new CompositionServiceProviderSnapshotBuilder()
            .AddServices(services)
            .Build(CompositionProviderBoundary.WorkflowRevision, "workflow-4");
        var registered = snapshot.GetRequiredKeyedService<IFlowSignalTarget>(Signal.Value);
        var first = FlowMessage.Create("ack");
        var second = FlowMessage.Create(42);

        (await registered.SendAsync(first)).ShouldBeTrue();
        (await registered.SendAsync(second)).ShouldBeTrue();

        target.TraceIds.ShouldBe([first.TraceId, second.TraceId]);
    }

    [Fact]
    public void Keyed_registration_extensions_reject_wrong_address_kinds()
    {
        var services = new ServiceCollection();
        var target = new BufferBlock<FlowMessage<string>>();

        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowResource<IValueService>(Component, _ => new ValueService("bad")));
        Should.Throw<ArgumentException>(() =>
            services.AddFluxFlowComponent(Resource, _ => new TestComponent()));
        Should.Throw<ArgumentException>(() =>
            services.AddExternalFluxFlowInputPort(ApplicationAddress.SystemEvents, target));
        Should.Throw<ArgumentException>(() =>
            services.AddExternalFluxFlowOutputPort(Resource, target));
        Should.Throw<ArgumentException>(() =>
            services.AddExternalFluxFlowSignalTarget(Resource, new RecordingSignalTarget()));

        services.AddExternalFluxFlowOutputPort(ApplicationAddress.SystemEvents, target)
            .ShouldBeSameAs(services);
    }

    [Fact]
    public void Snapshot_builder_rejects_invalid_inputs()
    {
        var builder = new CompositionServiceProviderSnapshotBuilder();
        var service = new TrackedService();

        Should.Throw<ArgumentNullException>(() => builder.AddServices(null!));
        Should.Throw<ArgumentNullException>(() => builder.ConfigureServices(null!));
        Should.Throw<ArgumentNullException>(() => builder.BridgeExternalSingleton<IExternalService>(null!));
        Should.Throw<ArgumentNullException>(() => builder.BridgeExternalKeyedSingleton("key", (IExternalService)null!));
        Should.Throw<ArgumentNullException>(() => builder.BridgeExternalKeyedSingleton(null!, service));
        Should.Throw<ArgumentException>(() => builder.BridgeExternalResource(Component, service));
        Should.Throw<ArgumentException>(() => builder.Build(CompositionProviderBoundary.Host, " "));
        Should.Throw<ArgumentNullException>(() =>
            CompositionServiceProviderSnapshot.CreateExternalHost("host", null!));
    }

    private interface IValueService
    {
        string Value { get; }
    }

    private interface IExternalService
    {
    }

    private sealed record ValueService(string Value = "value") : IValueService;

    private sealed class LateService;

    private sealed class MissingService;

    private sealed class InvalidService(MissingService missing)
    {
        public MissingService Missing { get; } = missing;
    }

    private sealed class ScopedService;

    private sealed class TrackedService : IExternalService, IDisposable, IAsyncDisposable
    {
        private int _disposeCount;

        public int DisposeCount => _disposeCount;

        public void Dispose() => Interlocked.Increment(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestComponent : IDataflowBlock, IAsyncDisposable
    {
        private readonly TransformBlock<FlowMessage<string>, FlowMessage<string>> _block =
            new(message => message.With(message.Value.ToUpperInvariant()));
        private int _disposeCount;

        public ITargetBlock<FlowMessage<string>> Input => _block;

        public ISourceBlock<FlowMessage<string>> Output => _block;

        public int DisposeCount => _disposeCount;

        public Task Completion => _block.Completion;

        public void Complete() => _block.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_block).Fault(exception);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCount) != 1)
                return;
            _block.Complete();
            await _block.Completion.ConfigureAwait(false);
        }
    }

    private class RecordingSignalTarget : IFlowSignalTarget
    {
        private readonly List<TraceId> _traceIds = [];

        public IReadOnlyList<TraceId> TraceIds
        {
            get
            {
                lock (_traceIds)
                    return _traceIds.ToArray();
            }
        }

        public Task Completion => Task.CompletedTask;

        public ValueTask<bool> SendAsync<T>(
            FlowMessage<T> signal,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(signal);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_traceIds)
                _traceIds.Add(signal.TraceId);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class DisposableSignalTarget :
        RecordingSignalTarget,
        IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private sealed class DisposableTargetBlock<T> : ITargetBlock<T>, IDisposable
    {
        private readonly BufferBlock<T> _block = new();

        public int DisposeCount { get; private set; }

        public Task Completion => _block.Completion;

        public void Complete() => _block.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_block).Fault(exception);

        public DataflowMessageStatus OfferMessage(
            DataflowMessageHeader messageHeader,
            T messageValue,
            ISourceBlock<T>? source,
            bool consumeToAccept)
            => ((ITargetBlock<T>)_block).OfferMessage(
                messageHeader,
                messageValue,
                source,
                consumeToAccept);

        public void Dispose() => DisposeCount++;
    }

    private sealed class DisposableSourceBlock<T> : ISourceBlock<T>, IDisposable
    {
        private readonly BufferBlock<T> _block = new();

        public int DisposeCount { get; private set; }

        public Task Completion => _block.Completion;

        public void Complete() => _block.Complete();

        public void Fault(Exception exception) => ((IDataflowBlock)_block).Fault(exception);

        public IDisposable LinkTo(ITargetBlock<T> target, DataflowLinkOptions linkOptions)
            => _block.LinkTo(target, linkOptions);

        [return: MaybeNull]
        public T ConsumeMessage(
            DataflowMessageHeader messageHeader,
            ITargetBlock<T> target,
            out bool messageConsumed)
            => ((ISourceBlock<T>)_block).ConsumeMessage(
                messageHeader,
                target,
                out messageConsumed);

        public bool ReserveMessage(
            DataflowMessageHeader messageHeader,
            ITargetBlock<T> target)
            => ((ISourceBlock<T>)_block).ReserveMessage(messageHeader, target);

        public void ReleaseReservation(
            DataflowMessageHeader messageHeader,
            ITargetBlock<T> target)
            => ((ISourceBlock<T>)_block).ReleaseReservation(messageHeader, target);

        public void Dispose() => DisposeCount++;
    }
}
