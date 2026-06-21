using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FluxFlow.Components.Mqtt.Contracts;
using Pulse.Mqtt.Resilience;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

public sealed record PulseMqttClientOptions
{
    public string? Host { get; init; }

    public int Port { get; init; } = 1883;

    public string? ClientId { get; init; }

    public string? ConnectionName { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public bool CleanStart { get; init; } = true;

    public bool UseTls { get; init; }

    public string? TlsTargetHost { get; init; }

    public X509CertificateCollection? ClientCertificates { get; init; }

    public RemoteCertificateValidationCallback? ServerCertificateValidation { get; init; }

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan? KeepAlivePeriod { get; init; }

    public bool AllowOfflinePublishQueue { get; init; }

    public bool QueueQos0WhenDisconnected { get; init; }

    public bool PropagateTraceContext { get; init; }

    public IMessageStore? MessageStore { get; init; }

    public ISessionStore? SessionStore { get; init; }

    public Dictionary<string, string> UserProperties { get; init; } = [];

    public PulseMqttLastWillOptions? LastWill { get; init; }
}

public sealed record PulseMqttLastWillOptions
{
    public required string Topic { get; init; }

    public byte[] Payload { get; init; } = [];

    public string? ContentType { get; init; }

    public MqttQualityOfService QualityOfService { get; init; } =
        MqttQualityOfService.AtMostOnce;

    public bool Retain { get; init; }

    public MqttPublishProperties? Properties { get; init; }
}
