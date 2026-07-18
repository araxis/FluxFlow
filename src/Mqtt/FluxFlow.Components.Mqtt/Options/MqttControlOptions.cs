namespace FluxFlow.Components.Mqtt.Options;

public sealed record MqttControlOptions
{
    public MqttRequestProcessing RequestProcessing { get; init; } =
        MqttRequestProcessing.Sequential;

    public MqttResultOrder ResultOrder { get; init; } = MqttResultOrder.PreserveInput;

    public int MaximumConcurrentRequests { get; init; } = 1;

    public int MaximumPendingRequests { get; init; } = 128;
}

public enum MqttRequestProcessing
{
    Sequential = 0,
    Concurrent = 1
}

public enum MqttResultOrder
{
    PreserveInput = 0,
    Completion = 1
}
