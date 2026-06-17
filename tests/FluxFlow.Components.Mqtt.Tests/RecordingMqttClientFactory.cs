using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Tests;

/// <summary>
/// Records every CreateAsync call. By default it hands out a single shared adapter
/// (so reconnect reuses the same adapter); pass a per-call factory to mint a fresh
/// adapter on each connect.
/// </summary>
internal sealed class RecordingMqttClientFactory : IMqttClientFactory
{
    private readonly object _gate = new();
    private readonly List<MqttClientFactoryContext> _contexts = [];
    private readonly Func<int, RecordingMqttClientAdapter> _adapterFactory;
    private readonly bool _ownLease;

    private int _createCalls;

    public RecordingMqttClientFactory(RecordingMqttClientAdapter adapter, bool ownLease = true)
        : this(_ => adapter, ownLease)
    {
    }

    public RecordingMqttClientFactory(
        Func<int, RecordingMqttClientAdapter> adapterFactory,
        bool ownLease = true)
    {
        _adapterFactory = adapterFactory;
        _ownLease = ownLease;
    }

    public int CreateCalls => Volatile.Read(ref _createCalls);

    public IReadOnlyList<MqttClientFactoryContext> Contexts
    {
        get
        {
            lock (_gate)
            {
                return _contexts.ToArray();
            }
        }
    }

    public ValueTask<MqttClientLease> CreateAsync(
        MqttClientFactoryContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = Interlocked.Increment(ref _createCalls) - 1;
        lock (_gate)
        {
            _contexts.Add(context);
        }

        var adapter = _adapterFactory(index);
        return ValueTask.FromResult(_ownLease
            ? MqttClientLease.Owned(adapter)
            : MqttClientLease.Shared(adapter));
    }
}

/// <summary>
/// A client factory for tests that never connect. The connection module requires a
/// factory, but config-only assertions and validation tests never call ConnectAsync.
/// </summary>
internal sealed class ThrowingMqttClientFactory : IMqttClientFactory
{
    public ValueTask<MqttClientLease> CreateAsync(
        MqttClientFactoryContext context,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "This test did not configure a client factory; it must not call ConnectAsync.");
}
