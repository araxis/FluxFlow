namespace FluxFlow.Components.Mqtt;

public static class MqttErrorCodes
{
    public const int PublishFailed = 2000;
    public const int PublishNotStarted = 2001;
    public const int PublishInvalidTopic = 2002;
    public const int PublishInvalidPayload = 2003;
    public const int PublishInvalidQualityOfService = 2004;
    public const int SubscribeFailed = 2100;
    public const int SubscribeStartupFailed = 2101;
    public const int SubscribeInvalidTopic = 2102;
}
