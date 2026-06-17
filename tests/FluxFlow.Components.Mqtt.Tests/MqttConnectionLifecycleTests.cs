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
}
