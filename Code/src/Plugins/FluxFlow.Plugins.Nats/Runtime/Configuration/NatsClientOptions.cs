using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace FluxFlow.Configuration.Components.Nats;

public static class NatsClientOptions
{
    public static NatsOpts CreateOptions(this NatsConnectionConfiguration settings, ILoggerFactory loggerFactory)
    {
        settings.Validate();
        return NatsOpts.Default with
        {
            Url = settings.Endpoint, LoggerFactory = loggerFactory,
            AuthOpts = new() { Username = ReadHost(settings.UsernameEnvironmentVariable), Password = ReadHost(settings.PasswordEnvironmentVariable),
                Token = ReadHost(settings.TokenEnvironmentVariable), CredsFile = ReadHost(settings.CredentialsFileEnvironmentVariable) },
            TlsOpts = new() { Mode = (TlsMode)settings.TlsMode, CaFile = ReadHost(settings.CaFileEnvironmentVariable), CertFile = ReadHost(settings.ClientCertificateEnvironmentVariable), KeyFile = ReadHost(settings.ClientKeyEnvironmentVariable) },
            ConnectTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds), CommandTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds), RequestTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            MaxReconnectRetry = settings.MaxReconnectAttempts, ReconnectWaitMin = TimeSpan.FromSeconds(settings.ReconnectWaitSeconds), ReconnectWaitMax = TimeSpan.FromSeconds(settings.ReconnectWaitSeconds),
            RetryOnInitialConnect = false, PublishTimeoutOnDisconnected = true, DrainSubscriptionsOnDispose = false
        };
    }
    private static string? ReadHost(string? name) => name is null ? null : Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Environment variable '{name}' is not available on the execution host.");
}
