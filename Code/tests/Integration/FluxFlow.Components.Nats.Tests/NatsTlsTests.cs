using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using NATS.Client.Core;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Nats.Tests;

public sealed class NatsTlsTests
{
    [Fact]
    public async Task Required_tls_uses_trusted_certificate_and_rejects_an_untrusted_server()
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=NATS test CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        using var key = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var leaf = leafRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        await using var broker = new ContainerBuilder("nats:2.11.8-alpine")
            .WithResourceMapping(Encoding.UTF8.GetBytes("port: 4222\ntls { cert_file: /tls/server.pem, key_file: /tls/server.key }\n"), "/etc/nats/nats.conf")
            .WithResourceMapping(Encoding.UTF8.GetBytes(leaf.ExportCertificatePem()), "/tls/server.pem")
            .WithResourceMapping(Encoding.UTF8.GetBytes(key.ExportPkcs8PrivateKeyPem()), "/tls/server.key")
            .WithCommand("-c", "/etc/nats/nats.conf").WithPortBinding(4222, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready")).Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await broker.StartAsync(deadline.Token);
        var caPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(caPath, root.ExportCertificatePem(), deadline.Token);
            var options = NatsOpts.Default with { Url = $"tls://127.0.0.1:{broker.GetMappedPublicPort(4222)}",
                TlsOpts = new() { Mode = TlsMode.Require, CaFile = caPath }, ConnectTimeout = TimeSpan.FromSeconds(3), RetryOnInitialConnect = false };
            await using (var trusted = new NatsSession(options))
                await (await trusted.OpenAsync(deadline.Token)).PingAsync(deadline.Token);
            await using var untrusted = new NatsSession(options with { TlsOpts = new() { Mode = TlsMode.Require } });
            await Should.ThrowAsync<NatsException>(() => untrusted.OpenAsync(deadline.Token));
        }
        finally { File.Delete(caPath); }
    }
}
