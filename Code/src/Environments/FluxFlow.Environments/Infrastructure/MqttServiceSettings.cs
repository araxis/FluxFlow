using System.Collections.Immutable;
using System.Security.Cryptography.X509Certificates;

namespace FluxFlow.Environments;

public sealed record MqttServiceSettings
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public MqttServiceCertificate? ClientCertificate { get; init; }
    public ImmutableArray<string> TrustedCertificates { get; init; } = [];
    public X509RevocationMode CertificateRevocationMode { get; init; } = X509RevocationMode.Online;
}

public sealed record MqttServiceCertificate
{
    public required string Path { get; init; }
    public string? Password { get; init; }
    public string? PrivateKeyPath { get; init; }
}
