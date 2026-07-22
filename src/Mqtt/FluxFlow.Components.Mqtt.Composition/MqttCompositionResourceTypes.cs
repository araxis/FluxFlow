namespace FluxFlow.Components.Mqtt.Composition;

public static class MqttCompositionResourceTypes
{
    public const string Broker = "mqtt.broker";

    public const string Client = "mqtt.client";

    public const string Subscription = "mqtt.subscription";

    public const string Retry = "retry.policy";
    public const string LegacyRetry = "resilience.retry";
}
