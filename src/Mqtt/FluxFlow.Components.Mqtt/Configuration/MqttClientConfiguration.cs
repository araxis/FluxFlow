using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace FluxFlow.Components.Mqtt.Configuration;

public sealed record MqttClientConfiguration
{
    private IReadOnlyList<MqttClientCertificate> _certificates = ImmutableArray<MqttClientCertificate>.Empty;
    private IReadOnlyDictionary<string, MqttSubscriptionDefinition> _subscriptions =
        ImmutableDictionary.Create<string, MqttSubscriptionDefinition>(StringComparer.Ordinal);

    public required string Name { get; init; }

    public required string ClientId { get; init; }

    public required MqttBrokerConfiguration Broker { get; init; }

    public MqttCredentialConfiguration? Credentials { get; init; }

    public IReadOnlyList<MqttClientCertificate> Certificates
    {
        get => _certificates;
        init => _certificates = value is null || value.Count == 0
            ? ImmutableArray<MqttClientCertificate>.Empty
            : value.ToImmutableArray();
    }

    public bool CleanStart { get; init; } = true;

    public TimeSpan KeepAlive { get; init; } = TimeSpan.FromSeconds(30);

    public MqttPublishMessage? LastWill { get; init; }

    public MqttAutoConnectMode AutoConnect { get; init; } = MqttAutoConnectMode.OnStart;

    public MqttReconnectConfiguration Reconnect { get; init; } = new();

    public IReadOnlyDictionary<string, MqttSubscriptionDefinition> Subscriptions
    {
        get => _subscriptions;
        init => _subscriptions = value is null || value.Count == 0
            ? ImmutableDictionary.Create<string, MqttSubscriptionDefinition>(StringComparer.Ordinal)
            : value.ToImmutableDictionary(StringComparer.Ordinal);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<MqttAutoConnectMode>))]
public enum MqttAutoConnectMode
{
    Disabled = 0,
    OnStart = 1
}
