using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FluxFlow.Components.Mqtt.Contracts;
using Pulse.Mqtt.Resilience;

namespace FluxFlow.Components.Mqtt.PulseMqtt;

public sealed record PulseMqttClientOptions
{
    private IReadOnlyDictionary<string, string> _userProperties =
        new Dictionary<string, string>(StringComparer.Ordinal);

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

    public IReadOnlyDictionary<string, string> UserProperties
    {
        get => _userProperties;
        init => _userProperties = CopyUserProperties(value);
    }

    public PulseMqttLastWillOptions? LastWill { get; init; }

    private static IReadOnlyDictionary<string, string> CopyUserProperties(
        IReadOnlyDictionary<string, string>? values)
        => values is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(values, StringComparer.Ordinal);
}

public sealed record PulseMqttLastWillOptions
{
    private byte[]? _payload = [];

    public required string Topic { get; init; }

    public byte[] Payload
    {
        get => _payload!;
        init => _payload = value?.ToArray();
    }

    public string? ContentType { get; init; }

    public MqttQualityOfService QualityOfService { get; init; } =
        MqttQualityOfService.AtMostOnce;

    public bool Retain { get; init; }

    public MqttPublishProperties? Properties { get; init; }
}
