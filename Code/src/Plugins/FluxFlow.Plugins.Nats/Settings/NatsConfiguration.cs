using FluxFlow.Components.Nats;
using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration.Components.Nats;

public sealed record NatsConnectionConfiguration
{
    public string Endpoint { get; init; } = "nats://localhost:4222";
    public string? UsernameEnvironmentVariable { get; init; }
    public string? PasswordEnvironmentVariable { get; init; }
    public string? TokenEnvironmentVariable { get; init; }
    public string? CredentialsFileEnvironmentVariable { get; init; }
    public NatsTlsMode TlsMode { get; init; } = NatsTlsMode.Auto;
    public string? CaFileEnvironmentVariable { get; init; }
    public string? ClientCertificateEnvironmentVariable { get; init; }
    public string? ClientKeyEnvironmentVariable { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    public int MaxReconnectAttempts { get; init; } = 10;
    public int ReconnectWaitSeconds { get; init; } = 2;

    public static NatsConnectionConfiguration Read(IConfiguration section)
    {
        foreach (var key in new[] { "Username", "Password", "Token", "Credentials", "Creds", "Seed", "NKey", "Jwt", "AuthOpts", "TlsOpts" })
            if (section.GetSection(key).Exists()) throw new ArgumentException("NATS credentials and private keys belong on the execution host. Use environment-variable references.");
        var configuration = section.Get<NatsConnectionConfiguration>() ?? throw new ArgumentException("Missing NATS connection settings.");
        configuration.Validate(); return configuration;
    }
    public void Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("nats" or "tls") ||
            uri.Host.Length == 0 || uri.Port == 0 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath is not ("" or "/"))
            throw new ArgumentException("NATS Endpoint requires nats://host[:port] or tls://host[:port], without embedded credentials or a path.");
        if (!Enum.IsDefined(TlsMode) || uri.Scheme == "tls" && TlsMode is NatsTlsMode.Disable or NatsTlsMode.Prefer)
            throw new ArgumentException("A tls:// endpoint requires TLS. Select Auto, Require or Implicit.");
        if (TimeoutSeconds is < 1 or > 600 || ReconnectWaitSeconds is < 1 or > 600 || MaxReconnectAttempts is < 1 or > 1000)
            throw new ArgumentException("NATS timeouts must be 1–600 seconds and reconnect attempts 1–1000.");
        if ((UsernameEnvironmentVariable is null) != (PasswordEnvironmentVariable is null)) throw new ArgumentException("Provide both username and password references.");
        if (new[] { UsernameEnvironmentVariable, TokenEnvironmentVariable, CredentialsFileEnvironmentVariable }.Count(value => value is not null) > 1)
            throw new ArgumentException("Choose one NATS authentication method.");
        if ((ClientCertificateEnvironmentVariable is null) != (ClientKeyEnvironmentVariable is null)) throw new ArgumentException("Provide both client certificate and key file references.");
        if (TlsMode == NatsTlsMode.Disable && new[] { CaFileEnvironmentVariable, ClientCertificateEnvironmentVariable, ClientKeyEnvironmentVariable }.Any(value => value is not null))
            throw new ArgumentException("Certificate settings cannot be used with disabled TLS.");
        foreach (var name in new[] { UsernameEnvironmentVariable, PasswordEnvironmentVariable, TokenEnvironmentVariable, CredentialsFileEnvironmentVariable,
                     CaFileEnvironmentVariable, ClientCertificateEnvironmentVariable, ClientKeyEnvironmentVariable }.OfType<string>())
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['=', '\0']) >= 0) throw new ArgumentException("Invalid environment-variable reference.");
    }

}

public record NatsCommandConfiguration : ComponentConfiguration
{
    public string Connection { get; init; } = "";
    public int Capacity { get; init; } = 128;
    public int MaxMessageBytes { get; init; } = 1048576;
    public int TimeoutSeconds { get; init; } = 30;
    public NatsComponentOptions Options()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Connection);
        var options = new NatsComponentOptions { Capacity = Capacity, MaxMessageBytes = MaxMessageBytes, TimeoutSeconds = TimeoutSeconds };
        options.Validate(); return options;
    }
}
public sealed record NatsSubscriberConfiguration : NatsCommandConfiguration
{
    public string Subject { get; init; } = "";
    public string? QueueGroup { get; init; }
}
