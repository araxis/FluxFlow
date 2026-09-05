namespace FluxFlow.Components.Mqtt.Client;

internal sealed class MqttClientResultFactory(TimeProvider clock)
{
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    internal MqttClientResult Failure(
        MqttClientOperation operation,
        string code,
        string message,
        bool isTransient,
        Exception? exception = null)
        => throw new MqttClientOperationException(
            operation,
            MqttClientErrors.Create(code, message, isTransient, exception));

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
