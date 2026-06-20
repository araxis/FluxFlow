using FluxFlow.Components.Mqtt.Contracts;

namespace FluxFlow.Components.Mqtt.MqttNet;

public sealed record MqttNetClientOptions
{
    public required string Host { get; init; }

    public int Port { get; init; } = 1883;

    public string? ClientId { get; init; }

    public string? ConnectionName { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public bool CleanSession { get; init; } = true;

    public bool UseTls { get; init; }

    public bool AllowUntrustedCertificates { get; init; }

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan? KeepAlivePeriod { get; init; }

    public bool AutomaticReconnect { get; init; } = true;

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    public Dictionary<string, string> UserProperties { get; init; } = [];

    public MqttNetLastWillOptions? LastWill { get; init; }
}

public sealed record MqttNetLastWillOptions
{
    public required string Topic { get; init; }

    public byte[] Payload { get; init; } = [];

    public string? ContentType { get; init; }

    public MqttQualityOfService QualityOfService { get; init; } =
        MqttQualityOfService.AtMostOnce;

    public bool Retain { get; init; }

    public MqttPublishProperties? Properties { get; init; }
}
