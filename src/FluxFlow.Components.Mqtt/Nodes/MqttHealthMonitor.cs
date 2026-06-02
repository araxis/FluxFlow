using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.Nodes;

internal sealed class MqttHealthMonitor : IAsyncDisposable
{
    private readonly IMqttClientHealthSource _healthSource;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action<MqttClientHealthEvent> _emitHealth;
    private readonly Action<MqttClientHealthEvent, Exception> _emitHealthFailure;
    private readonly Task _task;

    private MqttHealthMonitor(
        IMqttClientHealthSource healthSource,
        Action<MqttClientHealthEvent> emitHealth,
        Action<MqttClientHealthEvent, Exception> emitHealthFailure)
    {
        _healthSource = healthSource;
        _emitHealth = emitHealth;
        _emitHealthFailure = emitHealthFailure;
        _task = RunAsync(_cancellation.Token);
    }

    public static MqttHealthMonitor? Start(
        IMqttClientAdapter adapter,
        Action<MqttClientHealthEvent> emitHealth,
        Action<MqttClientHealthEvent, Exception> emitHealthFailure)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(emitHealth);
        ArgumentNullException.ThrowIfNull(emitHealthFailure);

        return adapter is IMqttClientHealthSource healthSource
            ? new MqttHealthMonitor(healthSource, emitHealth, emitHealthFailure)
            : null;
    }

    public void Cancel()
        => _cancellation.Cancel();

    public async ValueTask DisposeAsync()
    {
        Cancel();

        try
        {
            await _task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var health in _healthSource.Health
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                _emitHealth(health);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            var health = new MqttClientHealthEvent
            {
                State = MqttClientHealthState.Faulted,
                Reason = exception.Message
            };

            _emitHealthFailure(health, exception);
        }
    }
}
