using FluxFlow.Data;

namespace FluxFlow.Components.Mqtt.Client;

public static class MqttClientErrorCodes
{
    public const string NotStarted = "mqtt.client.not-started";
    public const string NotConnected = "mqtt.client.not-connected";
    public const string ConnectFailed = "mqtt.client.connect-failed";
    public const string DisconnectFailed = "mqtt.client.disconnect-failed";
    public const string PublishFailed = "mqtt.client.publish-failed";
    public const string SubscribeFailed = "mqtt.client.subscribe-failed";
    public const string UnsubscribeFailed = "mqtt.client.unsubscribe-failed";
    public const string InvalidRequest = "mqtt.client.invalid-request";
}

internal static class MqttClientErrors
{
    public static FlowError Create(
        string code,
        string message,
        bool isTransient,
        Exception? exception = null)
        => new(
            code,
            message,
            "Mqtt",
            isTransient,
            exception is null
                ? null
                : FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
                {
                    ["ExceptionType"] = FlowValue.From(exception.GetType().FullName ?? exception.GetType().Name)
                }));
}
