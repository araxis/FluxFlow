using MQTTnet;
using Polly.Retry;

namespace FluxFlow.Components.Mqtt;

public sealed class MqttClientResourceOptions
{
    public required MqttClientOptions Client { get; init; }
    public bool AutoConnect { get; init; } = true;
    /// <summary>Automatic connection retry policy. Null allows one attempt without retries.</summary>
    public RetryStrategyOptions? ReconnectPolicy { get; init; } = new()
    {
        MaxRetryAttempts = int.MaxValue, Delay = TimeSpan.FromSeconds(5)
    };
}

public sealed class MqttConnectionOptions
{
    public required MqttClientResource Connection { get; init; }
    public int OutputCapacity { get; init; } = 128;
}

public sealed class MqttPublisherOptions
{
    public required MqttClientResource Connection { get; init; }
    public int OutputCapacity { get; init; } = 128;
}

public sealed class MqttSubscriberOptions
{
    public required MqttClientResource Connection { get; init; }
    public IReadOnlyList<MqttTopic> Topics { get; init; } = Array.Empty<MqttTopic>();
    public int OutputCapacity { get; init; } = 128;
    public MqttAcknowledgementMode AcknowledgementMode { get; init; }
    public int MaxPendingAcknowledgements { get; init; } = 128;
}
