namespace FluxFlow.Components.Mqtt;

public static class MqttErrorCodes
{
    public const int PublishFailed = 2000;
    public const int PublishInvalidTopic = 2002;
    public const int PublishInvalidPayload = 2003;
    public const int PublishInvalidQualityOfService = 2004;
    public const int PublishTimedOut = 2005;
    public const int PublishNotConnected = 2006;
    public const int TriggerFailed = 2100;
    public const int TriggerStartupFailed = 2101;
    public const int TriggerNotConnected = 2103;
    public const int TriggerResponseTimedOut = 2104;
    public const int TriggerAcknowledgementFailed = 2105;
    public const int TriggerResponseFailed = 2106;
    public const int TriggerDuplicateCorrelation = 2107;
}
