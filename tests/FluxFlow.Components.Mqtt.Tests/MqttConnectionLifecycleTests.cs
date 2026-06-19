using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Diagnostics;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Nodes;
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
        await using var node = MqttTestContext.CreateConnection(factory);

        node.State.ShouldBe(MqttClientHealthState.Disconnected);
        node.TryGetAdapter(out _).ShouldBeFalse();

        await node.ConnectAsync();

        node.State.ShouldBe(MqttClientHealthState.Connected);
        factory.CreateCalls.ShouldBe(1);
        node.TryGetAdapter(out var borrowed).ShouldBeTrue();
        borrowed.ShouldBeSameAs(adapter);
        node.ConnectionEpoch.ShouldBe(1);

        // Idempotent: a second connect does not create a second client.
        await node.ConnectAsync();
        factory.CreateCalls.ShouldBe(1);
    }

    [Fact]
    public async Task DisconnectAsync_DisposesLeaseAndStopsBorrows()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: true);
        await using var node = MqttTestContext.CreateConnection(factory);

        await node.ConnectAsync();
        node.TryGetAdapter(out _).ShouldBeTrue();

        await node.DisconnectAsync();

        node.State.ShouldBe(MqttClientHealthState.Disconnected);
        node.TryGetAdapter(out _).ShouldBeFalse();
        adapter.DisposeCalls.ShouldBe(1);

        // Idempotent disconnect does not double-dispose.
        await node.DisconnectAsync();
        adapter.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ConnectAsync_IsSingleFlight_TwoConcurrentConnectsCreateOneClient()
    {
        var gate = new TaskCompletionSource();
        var adapter = new RecordingMqttClientAdapter();
        var factory = new GatedMqttClientFactory(adapter, gate.Task);
        await using var node = MqttTestContext.CreateConnection(factory);

        var first = node.ConnectAsync();
        var second = node.ConnectAsync();

        // Both calls observe the same in-flight establish; release the factory.
        gate.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        factory.CreateCalls.ShouldBe(1);
        node.State.ShouldBe(MqttClientHealthState.Connected);
        node.ConnectionEpoch.ShouldBe(1);
    }

    [Fact]
    public async Task DisconnectAsync_CancelsInFlightConnect()
    {
        var gate = new TaskCompletionSource();
        var adapter = new RecordingMqttClientAdapter();
        var factory = new GatedMqttClientFactory(adapter, gate.Task);
        await using var node = MqttTestContext.CreateConnection(factory);

        var connect = node.ConnectAsync();

        // Disconnect while the connect is parked inside the factory, then release it.
        await node.DisconnectAsync();
        gate.SetResult();

        try
        {
            await connect.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // The in-flight connect was cancelled by disconnect.
        }

        node.State.ShouldBe(MqttClientHealthState.Disconnected);
        node.TryGetAdapter(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task DisposeAsync_TearsDownLeaseIdempotently()
    {
        var adapter = new RecordingMqttClientAdapter();
        var factory = new RecordingMqttClientFactory(adapter, ownLease: true);
        var node = MqttTestContext.CreateConnection(factory);

        await node.ConnectAsync();
        await node.DisposeAsync();

        adapter.DisposeCalls.ShouldBe(1);

        // Idempotent dispose.
        await node.DisposeAsync();
        adapter.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ConnectAsync_WhenCreateFaults_EntersFaultedThenSucceedsOnRetry()
    {
        // First CreateAsync throws a transient fault; ConnectAsync surfaces it and the
        // node enters Faulted, emitting a faulted health event, but the node is not
        // disposed (it stays runnable so a retry can succeed).
        var adapter = new RecordingMqttClientAdapter();
        var factory = new FaultThenSucceedMqttClientFactory(adapter, faults: 1);
        await using var node = MqttTestContext.CreateConnection(factory);

        var events = MqttTestContext.Sink(node.Events);

        await Should.ThrowAsync<InvalidOperationException>(
            node.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        node.State.ShouldBe(MqttClientHealthState.Faulted);
        node.TryGetAdapter(out _).ShouldBeFalse();
        factory.CreateCalls.ShouldBe(1);

        var faultedEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        faultedEvent.Name.ShouldBe(MqttEventNames.ConnectionHealthChanged);
        faultedEvent.Level.ShouldBe(FlowEventLevel.Error);
        faultedEvent.Attributes["state"].ShouldBe(MqttClientHealthState.Faulted.ToString());

        // Retry: the factory now succeeds, so the adapter becomes borrowable.
        await node.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));

        node.State.ShouldBe(MqttClientHealthState.Connected);
        factory.CreateCalls.ShouldBe(2);
        node.TryGetAdapter(out var borrowed).ShouldBeTrue();
        borrowed.ShouldBeSameAs(adapter);
    }

    [Fact]
    public async Task DisconnectAsync_WinningDuringEstablish_DisposesFreshLeaseOnce()
    {
        // Gate CreateAsync so the disconnect lands while the create is still in flight.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new RecordingMqttClientAdapter();
        var factory = new GatedSignalingMqttClientFactory(adapter, gate.Task, ownLease: true);
        var node = MqttTestContext.CreateConnection(factory);

        var connect = node.ConnectAsync();
        await factory.Created.WaitAsync(TimeSpan.FromSeconds(5));

        // Disconnect wins the race (sets userDisconnected), then the parked create is
        // released and returns a real, Owned lease.
        await node.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
        gate.SetResult();
        await connect.WaitAsync(TimeSpan.FromSeconds(5));

        // The freshly built (Owned) lease must be dropped, not published, and disposed
        // exactly once; the adapter is never observable.
        node.State.ShouldBe(MqttClientHealthState.Disconnected);
        node.TryGetAdapter(out _).ShouldBeFalse();
        adapter.DisposeCalls.ShouldBe(1);

        await node.DisposeAsync();

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
        var node = MqttTestContext.CreateConnection(factory);

        var connect = node.ConnectAsync();
        await factory.Created.WaitAsync(TimeSpan.FromSeconds(5));

        await node.DisposeAsync();
        gate.SetResult();

        // The in-flight connect completes without surfacing an unobserved exception, and
        // the freshly built adapter is disposed (no leak) rather than published.
        await connect.WaitAsync(TimeSpan.FromSeconds(5));

        node.TryGetAdapter(out _).ShouldBeFalse();
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
        await using var node = MqttTestContext.CreateConnection(factory);

        var events = MqttTestContext.Sink(node.Events);

        await node.ConnectAsync();
        adapter.PushHealth(new MqttClientHealthEvent
        {
            State = MqttClientHealthState.Connected,
            ConnectionName = MqttTestContext.ConnectionName
        });

        var healthEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        healthEvent.Name.ShouldBe(MqttEventNames.ConnectionHealthChanged);
        healthEvent.Attributes["state"].ShouldBe(MqttClientHealthState.Connected.ToString());
        healthEvent.Attributes["connectionName"].ShouldBe(MqttTestContext.ConnectionName);
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
