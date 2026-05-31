namespace FluxFlow.Components.Mqtt.Contracts;

public sealed class MqttClientLease : IAsyncDisposable
{
    private bool _disposed;

    public MqttClientLease(
        IMqttClientAdapter adapter,
        bool disposeAdapter)
    {
        Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        DisposeAdapter = disposeAdapter;
    }

    public IMqttClientAdapter Adapter { get; }

    public bool DisposeAdapter { get; }

    public static MqttClientLease Owned(IMqttClientAdapter adapter)
        => new(adapter, disposeAdapter: true);

    public static MqttClientLease Shared(IMqttClientAdapter adapter)
        => new(adapter, disposeAdapter: false);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (DisposeAdapter)
        {
            await Adapter.DisposeAsync().ConfigureAwait(false);
        }
    }
}
