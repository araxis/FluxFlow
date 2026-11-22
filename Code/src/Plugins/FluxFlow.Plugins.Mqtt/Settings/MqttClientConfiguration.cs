using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Polly.Retry;
using System.Security.Cryptography.X509Certificates;
using Polly;

namespace FluxFlow.Configuration.Components.Mqtt;

public sealed class MqttClientConfiguration
{
    public string? Address { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public required string ClientId { get; init; }
    public bool AutoConnect { get; init; } = true;
    public MqttReconnectConfiguration Reconnect { get; init; } = new();
    public string? Username { get; init; }
    public string? Password { get; init; }
    public bool? Tls { get; init; }
    public MqttCertificateConfiguration? ClientCertificate { get; init; }
    public string[] TrustedCertificates { get; init; } = [];
    public X509RevocationMode CertificateRevocationMode { get; init; } = X509RevocationMode.Online;
    public bool CleanSession { get; init; } = true;
    public uint SessionExpiryInterval { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);

    public (Uri Address, Polly.Retry.RetryStrategyOptions Retry) Validate(Microsoft.Extensions.Configuration.IConfiguration section)
    {
        if (section.GetSection("ReconnectInterval").Exists())
            throw new ArgumentException($"'ReconnectInterval' is replaced by Reconnect.Delay and the reconnect policy settings.");
        return Validate();
    }

    public (Uri Address, RetryStrategyOptions Retry) Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientId);
        if (Timeout <= TimeSpan.Zero || Timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(Timeout));
        var retry = Reconnect;
        ArgumentOutOfRangeException.ThrowIfNegative(retry.MaxRetryAttempts);
        var policy = new RetryStrategyOptions
        {
            MaxRetryAttempts = Math.Max(1, retry.MaxRetryAttempts), Delay = retry.Delay,
            BackoffType = retry.BackoffType, UseJitter = retry.UseJitter, MaxDelay = retry.MaxDelay
        };
        Validator.ValidateObject(policy, new ValidationContext(policy), validateAllProperties: true);
        if (!Enum.IsDefined(retry.BackoffType)) throw new ArgumentOutOfRangeException(nameof(retry.BackoffType));
        if (!Enum.IsDefined(CertificateRevocationMode)) throw new ArgumentOutOfRangeException(nameof(CertificateRevocationMode));
        if (Password is not null && Username is null)
            throw new ArgumentException("MQTT Password requires Username.");
        var uri = MqttClientConfiguration.ResolveAddress(Address, Host, Port, Tls);
        var secure = uri.Scheme is "mqtts" or "wss";
        if (!secure && (ClientCertificate is not null || TrustedCertificates.Length != 0))
            throw new ArgumentException("MQTT certificates require mqtts://, wss:// or Tls enabled with Host.");

        if (ClientCertificate is { } cert)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cert.Path);
            if (cert.PrivateKeyPath != null) ArgumentException.ThrowIfNullOrWhiteSpace(cert.PrivateKeyPath);
        }
        foreach (var path in TrustedCertificates) ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return (uri, policy);
    }

    /// <summary>Resolves the configured endpoint and checks transport settings without opening a connection.</summary>
    public static Uri ResolveAddress(string? address, string? host = null, int? port = null, bool? tls = null)
    {
        Uri uri;
        if (address is not null)
        {
            if (host is not null || port is not null)
                throw new ArgumentException("Specify MQTT Address or Host/Port, not both.");
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri!))
                throw new ArgumentException("MQTT Address must be an absolute mqtt, mqtts, ws or wss URI.");
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(host, "config.Host");
            if (Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown)
                throw new ArgumentException("MQTT Host must be a hostname or IP address; use Address for a URI.");
            if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            uri = new UriBuilder(tls == true ? "mqtts" : "mqtt", host, port ?? -1).Uri;
        }
        if (uri.Scheme is not ("mqtt" or "mqtts" or "tcp" or "ws" or "wss") || string.IsNullOrEmpty(uri.Host) || uri.Port == 0)
            throw new ArgumentException("MQTT Address requires mqtt, mqtts, tcp, ws or wss, a host and a valid port.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Use MQTT Username and Password instead of credentials in Address.");
        if (!string.IsNullOrEmpty(uri.Fragment)) throw new ArgumentException("MQTT Address cannot contain a fragment.");
        if (uri.Scheme is "mqtt" or "mqtts" or "tcp" && (uri.AbsolutePath is not ("" or "/") || uri.Query.Length != 0))
            throw new ArgumentException("A TCP MQTT address cannot contain a path or query. Use ws or wss for WebSockets.");
        if (tls is { } explicitTls && explicitTls != (uri.Scheme is "mqtts" or "wss"))
            throw new ArgumentException("MQTT Tls conflicts with the Address scheme.");
        return uri;
    }
}

public sealed class MqttReconnectConfiguration
{
    public int MaxRetryAttempts { get; init; } = int.MaxValue;
    public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(5);
    public DelayBackoffType BackoffType { get; init; } = DelayBackoffType.Constant;
    public bool UseJitter { get; init; }
    public TimeSpan? MaxDelay { get; init; }
}

public sealed class MqttCertificateConfiguration
{
    public required string Path { get; init; }
    public string? Password { get; init; }
    public string? PrivateKeyPath { get; init; }
}
