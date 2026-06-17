using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttConnectionLifecycleTests
{
    [Fact]
    public async Task ConnectAsync_EstablishesLeaseOnceAndExposesAdapter()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: false);
        var handle = CreateHandle(factory);

        handle.State.ShouldBe(MqttClientHealthState.Disconnected);
        handle.TryGetAdapter(out _).ShouldBeFalse();

        await handle.ConnectAsync();

        handle.State.ShouldBe(MqttClientHealthState.Connected);
        factory.CreateCalls.ShouldBe(1);
        handle.TryGetAdapter(out var borrowed).ShouldBeTrue();
        borrowed.ShouldBeSameAs(adapter);
        handle.ConnectionEpoch.ShouldBe(1);

        // Idempotent: a second connect does not create a second client.
        await handle.ConnectAsync();
        factory.CreateCalls.ShouldBe(1);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_DisposesLeaseAndStopsBorrows()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: true);
        var handle = CreateHandle(factory);

        await handle.ConnectAsync();
        handle.TryGetAdapter(out _).ShouldBeTrue();

        await handle.DisconnectAsync();

        handle.State.ShouldBe(MqttClientHealthState.Disconnected);
        handle.TryGetAdapter(out _).ShouldBeFalse();
        adapter.DisposeCalls.ShouldBe(1);

        // Idempotent disconnect does not double-dispose.
        await handle.DisconnectAsync();
        adapter.DisposeCalls.ShouldBe(1);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_IsSingleFlight_TwoConcurrentConnectsCreateOneClient()
    {
        var gate = new TaskCompletionSource();
        var adapter = new RecordingMqttClientAdapter();
        var factory = new GatedMqttClientFactory(adapter, gate.Task);
        var handle = CreateHandle(factory);

        var first = handle.ConnectAsync();
        var second = handle.ConnectAsync();

        // Both calls observe the same in-flight establish; release the factory.
        gate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        factory.CreateCalls.ShouldBe(1);
        handle.State.ShouldBe(MqttClientHealthState.Connected);
        handle.ConnectionEpoch.ShouldBe(1);

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_CancelsInFlightConnect()
    {
        var gate = new TaskCompletionSource();
        var adapter = new RecordingMqttClientAdapter();
        var factory = new GatedMqttClientFactory(adapter, gate.Task);
        var handle = CreateHandle(factory);

        var connect = handle.ConnectAsync();

        // Disconnect while the connect is parked inside the factory, then release it.
        await handle.DisconnectAsync();
        gate.SetResult();

        try
        {
            await connect.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // The in-flight connect was cancelled by disconnect.
        }

        handle.State.ShouldBe(MqttClientHealthState.Disconnected);
        handle.TryGetAdapter(out _).ShouldBeFalse();

        await ((IAsyncDisposable)handle).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_TearsDownLeaseIdempotently()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: true);
        var handle = CreateHandle(factory);

        await handle.ConnectAsync();
        await ((IAsyncDisposable)handle).DisposeAsync();

        adapter.DisposeCalls.ShouldBe(1);

        // Idempotent dispose.
        await ((IAsyncDisposable)handle).DisposeAsync();
        adapter.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ConnectAsync_WhenCreateFaults_EntersFaultedThenSucceedsOnRetry()
    {
        // First CreateAsync throws a transient fault; ConnectAsync surfaces it and the
        // node enters Faulted, emits a faulted health event + error diagnostic, but the
        // resource node is NOT faulted (it stays runnable so a retry can succeed).
        var adapter = new RecordingMqttClientAdapter();
        var factory = new FaultThenSucceedMqttClientFactory(adapter, faults: 1);
        var registry = MqttResourceTestContext.CreateRegistry(clientFactory: factory);
        var resources = MqttResourceTestContext.CreateResources(registry);
        var node = (Nodes.MqttConnectionNode)resources[
            new NodeName(MqttResourceTestContext.ConnectionName)].Node;

        var events = new BufferBlock<FlowEvent>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        node.Events.LinkTo(events);
        node.Diagnostics.LinkTo(diagnostics);

        await Should.ThrowAsync<InvalidOperationException>(
            node.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        node.State.ShouldBe(MqttClientHealthState.Faulted);
        node.TryGetAdapter(out _).ShouldBeFalse();
        factory.CreateCalls.ShouldBe(1);

        // A connect fault must not fault the resource node.
        node.Completion.IsCompleted.ShouldBeFalse();

        var faultedEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        faultedEvent.Type.ShouldBe(MqttEventNames.ConnectionHealthChanged);
        faultedEvent.Status.ShouldBe(MqttClientHealthState.Faulted.ToString());

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(MqttDiagnosticNames.ConnectionHealthChanged);
        diagnostic.Level.ShouldBe(FlowDiagnosticLevel.Error);

        // Retry: the factory now succeeds, so the adapter becomes borrowable.
        await node.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

        node.State.ShouldBe(MqttClientHealthState.Connected);
        factory.CreateCalls.ShouldBe(2);
        node.TryGetAdapter(out var borrowed).ShouldBeTrue();
        borrowed.ShouldBeSameAs(adapter);

        await ((IAsyncDisposable)node).DisposeAsync();
    }

    [Fact]
    public async Task DisconnectAsync_WinningDuringEstablish_DisposesFreshLeaseOnce()
    {
        // Gate CreateAsync so the disconnect lands while the create is still in flight.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new RecordingMqttClientAdapter();
        var factory = new GatedSignalingMqttClientFactory(adapter, gate.Task, ownLease: true);
        var handle = CreateHandle(factory);

        var connect = handle.ConnectAsync();
        await factory.Created.WaitAsync(TimeSpan.FromSeconds(5));

        // Disconnect wins the race (sets userDisconnected), then the parked create is
        // released and returns a real, Owned lease.
        await handle.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult();
        await connect.WaitAsync(TimeSpan.FromSeconds(5));

        // The freshly built (Owned) lease must be dropped, not published, and disposed
        // exactly once; the adapter is never observable.
        handle.State.ShouldBe(MqttClientHealthState.Disconnected);
        handle.TryGetAdapter(out _).ShouldBeFalse();
        adapter.DisposeCalls.ShouldBe(1);

        await ((IAsyncDisposable)handle).DisposeAsync();

        // Dispose after a dropped establish must not double-dispose the adapter.
        adapter.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeAsync_RacingInFlightConnect_DisposesFreshLeaseWithoutLeak()
    {
        // Gate CreateAsync so DisposeAsync disposes the gate while the create is still in
        // flight; on resume the publish guard must drop and dispose the fresh lease even
        // though re-acquiring the disposed gate throws ObjectDisposedException.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new RecordingMqttClientAdapter();
        var factory = new GatedSignalingMqttClientFactory(adapter, gate.Task, ownLease: true);
        var handle = CreateHandle(factory);

        var connect = handle.ConnectAsync();
        await factory.Created.WaitAsync(TimeSpan.FromSeconds(5));

        await ((IAsyncDisposable)handle).DisposeAsync();
        gate.SetResult();

        // The in-flight connect completes without surfacing an unobserved exception, and
        // the freshly built adapter is disposed (no leak) rather than published.
        await connect.WaitAsync(TimeSpan.FromSeconds(5));

        handle.TryGetAdapter(out _).ShouldBeFalse();
        adapter.DisposeCalls.ShouldBe(1);

        // Force any unobserved-task finalizers to surface a missed exception.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [Fact]
    public async Task HealthEventsAreEmittedOnConnect()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: false);
        var registry = MqttResourceTestContext.CreateRegistry(clientFactory: factory);
        var resources = MqttResourceTestContext.CreateResources(registry);
        var node = (Nodes.MqttConnectionNode)resources[new NodeName(MqttResourceTestContext.ConnectionName)].Node;

        var events = new BufferBlock<FlowEvent>();
        node.Events.LinkTo(events);

        await node.ConnectAsync();
        adapter.PushHealth(new MqttClientHealthEvent
        {
            State = MqttClientHealthState.Connected,
            ConnectionName = MqttResourceTestContext.ConnectionName
        });

        var healthEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        healthEvent.Type.ShouldBe(MqttEventNames.ConnectionHealthChanged);
        healthEvent.Status.ShouldBe(MqttClientHealthState.Connected.ToString());

        await ((IAsyncDisposable)node).DisposeAsync();
    }

    private static IMqttConnectionHandle CreateHandle(IMqttClientFactory factory)
    {
        var registry = MqttResourceTestContext.CreateRegistry(clientFactory: factory);
        var resources = MqttResourceTestContext.CreateResources(registry);
        return MqttResourceTestContext.ResolveHandle(resources);
    }

    private sealed class GatedMqttClientFactory(RecordingMqttClientAdapter adapter, Task gate)
        : IMqttClientFactory
    {
        private int _createCalls;

        public int CreateCalls => Volatile.Read(ref _createCalls);

        public async ValueTask<MqttClientLease> CreateAsync(
            MqttClientFactoryContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCalls);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return MqttClientLease.Shared(adapter);
        }
    }

    /// <summary>
    /// Parks every CreateAsync inside a caller-supplied gate and signals when the create
    /// has been entered, so a test can race a disconnect/dispose against an in-flight
    /// create before releasing the gate. The gate is awaited with CancellationToken.None
    /// so a cancelled connect token cannot short the create before the racing teardown
    /// runs and the publish guard drops the fresh lease (the leak/teardown vectors under
    /// test). Hands out an Owned lease so a dropped lease's adapter is disposed.
    /// </summary>
    private sealed class GatedSignalingMqttClientFactory(
        RecordingMqttClientAdapter adapter,
        Task gate,
        bool ownLease)
        : IMqttClientFactory
    {
        private readonly TaskCompletionSource _created =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _createCalls;

        public int CreateCalls => Volatile.Read(ref _createCalls);

        /// <summary>Completes once CreateAsync has been entered (before the gate is awaited).</summary>
        public Task Created => _created.Task;

        public async ValueTask<MqttClientLease> CreateAsync(
            MqttClientFactoryContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCalls);
            _created.TrySetResult();

            await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            return ownLease
                ? MqttClientLease.Owned(adapter)
                : MqttClientLease.Shared(adapter);
        }
    }

    /// <summary>
    /// Throws from CreateAsync until the configured number of failures is exhausted, then
    /// hands out the adapter as an Owned lease. Models a transient connect fault, so a
    /// retry can succeed.
    /// </summary>
    private sealed class FaultThenSucceedMqttClientFactory(
        RecordingMqttClientAdapter adapter,
        int faults)
        : IMqttClientFactory
    {
        private int _remainingFaults = faults;
        private int _createCalls;

        public int CreateCalls => Volatile.Read(ref _createCalls);

        public ValueTask<MqttClientLease> CreateAsync(
            MqttClientFactoryContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _createCalls);

            if (Interlocked.Decrement(ref _remainingFaults) >= 0)
            {
                throw new InvalidOperationException("MQTT client failed to connect (transient).");
            }

            return ValueTask.FromResult(MqttClientLease.Owned(adapter));
        }
    }
}
