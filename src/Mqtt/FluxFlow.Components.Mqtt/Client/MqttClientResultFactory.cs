namespace FluxFlow.Components.Mqtt.Client;

internal sealed class MqttClientResultFactory(TimeProvider clock)
{
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    internal MqttClientFailureResult Failure(
        MqttClientOperation operation,
        string code,
        string message,
        bool isTransient,
        Exception? exception = null)
        => new(
            operation,
            MqttClientErrors.Create(code, message, isTransient, exception),
            _clock.GetUtcNow());

    internal static string ErrorCodeFor(MqttClientOperation operation)
        => operation switch
        {
            MqttClientOperation.Connect => MqttClientErrorCodes.ConnectFailed,
            MqttClientOperation.Disconnect => MqttClientErrorCodes.DisconnectFailed,
            MqttClientOperation.Publish => MqttClientErrorCodes.PublishFailed,
            MqttClientOperation.Subscribe => MqttClientErrorCodes.SubscribeFailed,
            MqttClientOperation.Unsubscribe => MqttClientErrorCodes.UnsubscribeFailed,
            _ => MqttClientErrorCodes.InvalidRequest
        };
}
