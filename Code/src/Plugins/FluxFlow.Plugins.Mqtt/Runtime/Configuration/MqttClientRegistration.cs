using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Mqtt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using Polly.Retry;
using System.ComponentModel.DataAnnotations;

namespace FluxFlow.Configuration.Components.Mqtt;

internal static class MqttClientRegistration
{
    internal static void Register(IServiceCollection services, ApplicationAddress address, IConfigurationSection section, string directory)
    {
        var config = section.Get<MqttClientConfiguration>() ?? throw new ArgumentException($"Missing MQTT settings at '{section.Path}'.");
        Register(services, address, config, directory, config.Validate(section));
    }

    internal static void Register(IServiceCollection services, ApplicationAddress address, MqttClientConfiguration config, string directory,
        (Uri Address, RetryStrategyOptions Retry) validation)
    {
        var (uri, policy) = validation;
        var secure = uri.Scheme is "mqtts" or "wss";
        var retry = config.Reconnect;

        // Native DI owns each certificate, created before the client and disposed after it.
        var clientKey = (address.Value, nameof(config.ClientCertificate));
        if (config.ClientCertificate is { } certificate)
        {
            var path = CertificatePath(certificate.Path, directory);
            var keyPath = certificate.PrivateKeyPath is null ? null : CertificatePath(certificate.PrivateKeyPath, directory);
            services.AddKeyedSingleton<X509Certificate2>(clientKey, (_, _) => LoadClientCertificate(path, keyPath, certificate.Password));
        }
        var trustKeys = config.TrustedCertificates.Select((path, index) =>
        {
            var fullPath = CertificatePath(path, directory);
            var key = (address.Value, nameof(config.TrustedCertificates), index);
            services.AddKeyedSingleton<X509Certificate2>(key, (_, _) => LoadAuthority(fullPath));
            return key;
        }).ToArray();
        services.AddFluxFlowResource(address, provider =>
        {
            var options = new MqttClientOptionsBuilder().WithConnectionUri(uri)
                .WithClientId(config.ClientId).WithCleanSession(config.CleanSession)
                .WithSessionExpiryInterval(config.SessionExpiryInterval).WithTimeout(config.Timeout);
            if (config.Username is not null) options.WithCredentials(config.Username, config.Password);
            if (secure)
            {
                var roots = trustKeys.Select(key => provider.GetRequiredKeyedService<X509Certificate2>(key)).ToArray();
                options.WithTlsOptions(tls =>
                {
                    tls.UseTls().WithSslProtocols(SslProtocols.None).WithRevocationMode(config.CertificateRevocationMode)
                        .WithCertificateValidationHandler(args => ValidateServer(args, roots, config.CertificateRevocationMode));
                    if (config.ClientCertificate is not null)
                        tls.WithClientCertificates([provider.GetRequiredKeyedService<X509Certificate2>(clientKey)]);
                });
            }
            return new MqttClientResource(new()
            {
                Client = options.Build(), AutoConnect = config.AutoConnect,
                ReconnectPolicy = retry.MaxRetryAttempts == 0 ? null : policy
            });
        });
    }

    private static string CertificatePath(string path, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path, directory);
    }

    private static X509Certificate2 LoadClientCertificate(string path, string? keyPath, string? password)
    {
        X509Certificate2 certificate;
        if (keyPath is not null || Path.GetExtension(path).Equals(".pem", StringComparison.OrdinalIgnoreCase))
        {
            using var pem = password is null ? X509Certificate2.CreateFromPemFile(path, keyPath)
                : X509Certificate2.CreateFromEncryptedPemFile(path, password, keyPath);
            // Reimport so Windows TLS can acquire a native key handle; disposal releases the temporary key.
            certificate = LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
        }
        else certificate = LoadPkcs12(File.ReadAllBytes(path), password);
        if (certificate.HasPrivateKey) return certificate;
        certificate.Dispose();
        throw new ArgumentException("The MQTT client certificate must include its private key.");
    }

    private static X509Certificate2 LoadPkcs12(byte[] data, string? password)
    {
        try
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(data, password, X509KeyStorageFlags.DefaultKeySet);
#else
            return new X509Certificate2(data, password, X509KeyStorageFlags.DefaultKeySet);
#endif
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    private static X509Certificate2 LoadAuthority(string path)
    {
#if NET9_0_OR_GREATER
        var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
#else
        var certificate = new X509Certificate2(path);
#endif
        if (certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority)) return certificate;
        certificate.Dispose();
        throw new ArgumentException("TrustedCertificates must contain CA certificates.");
    }

    private static bool ValidateServer(MqttClientCertificateValidationEventArgs args, X509Certificate2[] roots, X509RevocationMode revocation)
    {
        if ((args.SslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None
            || args.Certificate is not X509Certificate2 certificate) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = revocation;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1")); // TLS server authentication.
        if (roots.Length != 0)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        }
        if (args.Chain is { } suppliedChain)
            foreach (var element in suppliedChain.ChainElements.Cast<X509ChainElement>().Skip(1))
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        return chain.Build(certificate);
    }
}
