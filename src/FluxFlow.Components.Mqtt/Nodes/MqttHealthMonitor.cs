using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Timing;

namespace FluxFlow.Components.Mqtt.Nodes;

internal sealed class MqttHealthMonitor : IAsyncDisposable
{
    private readonly IMqttClientHealthSource _healthSource;
    private readonly IMqttClock _clock;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action<MqttClientHealthEvent> _emitHealth;
    private readonly Action<MqttClientHealthEvent, Exception> _emitHealthFailure;
    private readonly Task _task;

    private MqttHealthMonitor(
        IMqttClientHealthSource healthSource,
        IMqttClock clock,
        Action<MqttClientHealthEvent> emitHealth,
        Action<MqttClientHealthEvent, Exception> emitHealthFailure)
    {
        _healthSource = healthSource;
        _clock = clock;
        _emitHealth = emitHealth;
        _emitHealthFailure = emitHealthFailure;
        _task = RunAsync(_cancellation.Token);
    }

    public static MqttHealthMonitor? Start(
        IMqttClientAdapter adapter,
        IMqttClock clock,
        Action<MqttClientHealthEvent> emitHealth,
        Action<MqttClientHealthEvent, Exception> emitHealthFailure)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(emitHealth);
        ArgumentNullException.ThrowIfNull(emitHealthFailure);

        return adapter is IMqttClientHealthSource healthSource
            ? new MqttHealthMonitor(healthSource, clock, emitHealth, emitHealthFailure)
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
                Timestamp = _clock.UtcNow,
                State = MqttClientHealthState.Faulted,
                Reason = exception.Message
            };

            _emitHealthFailure(health, exception);
        }
    }
}
