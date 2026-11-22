using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Mqtt;
using FluxFlow.Core.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MQTTnet.AspNetCore;
using MQTTnet.Protocol;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttTransportTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("mqtt", false)]
    [InlineData("mqtts", false)]
    [InlineData("ws", false)]
    [InlineData("wss", false)]
    [InlineData("mqtts", true)]
    [InlineData("wss", true)]
    public async Task Configured_transport_credentials_and_certificates_deliver_messages(string scheme, bool pem)
    {
        using var certificates = new Certificates();
        await using var server = await StartServer(scheme, certificates);
        var secure = scheme is "mqtts" or "wss";
        var settings = Settings(ServerAddress(server, scheme));
        if (secure)
        {
            settings["TrustedCertificates"] = new[] { "ca.pem" };
            settings["ClientCertificate"] = pem
                ? new { Path = "client.pem", Password = "key-password", PrivateKeyPath = "client.key" }
                : (object)new { Path = "client.pfx", Password = "pfx-password" };
        }
        await using var provider = Register(settings, certificates.Directory).BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var input = provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.connection.Input");
        var replies = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.connection.Output"));
        var received = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.subscriber.Output"));
        var publishReplies = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.publisher.Output"));
        await app.StartAsync();
        await input.SendAsync(Command("Connect"));
        var reply = await replies.ReceiveAsync(Deadline);
        reply.IsError.ShouldBeFalse(reply.Error?.Message);
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.publisher.Input").SendAsync(
            FlowMessage.Create(new MqttPublishRequest { Topic = "test/data", Payload = "hello", QoS = 1 }));
        (await publishReplies.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        (await received.ReceiveAsync(Deadline)).Value.Json.GetProperty("Text").GetString().ShouldBe("hello");
        await app.StopAsync().WaitAsync(Deadline);
    }

    [Theory]
    [InlineData("mqtts", "untrusted")]
    [InlineData("wss", "untrusted")]
    [InlineData("mqtts", "hostname")]
    [InlineData("wss", "hostname")]
    [InlineData("mqtts", "expired")]
    [InlineData("wss", "expired")]
    [InlineData("mqtts", "client")]
    [InlineData("wss", "client")]
    [InlineData("mqtt", "password")]
    [InlineData("ws", "password")]
    public async Task Invalid_server_or_client_authentication_is_rejected(string scheme, string failure)
    {
        using var certificates = new Certificates(failure == "hostname", failure == "expired");
        await using var server = await StartServer(scheme, certificates);
        var settings = Settings(ServerAddress(server, scheme));
        if (failure != "untrusted" && scheme is "mqtts" or "wss") settings["TrustedCertificates"] = new[] { "ca.pem" };
        if (failure != "client" && scheme is "mqtts" or "wss")
            settings["ClientCertificate"] = new { Path = "client.pfx", Password = "pfx-password" };
        if (failure == "password") settings["Password"] = "incorrect";
        await using var provider = Register(settings, certificates.Directory).BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var replies = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.connection.Output"));
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.connection.Input").SendAsync(Command("Connect"));
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        provider.GetRequiredFlowResource<MqttClientResource>("broker").IsConnected.ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://localhost/mqtt", null)]
    [InlineData("mqtt://localhost/path", null)]
    [InlineData("mqtt://localhost?query=1", null)]
    [InlineData("ws://localhost/mqtt#fragment", null)]
    [InlineData("mqtt://username:password@localhost", null)]
    [InlineData("wss://localhost/mqtt", false)]
    [InlineData("ws://localhost/mqtt", true)]
    [InlineData("mqtt://localhost:0", null)]
    public void Invalid_addresses_are_rejected_before_resource_creation(string address, bool? tls)
    {
        var settings = Settings(address);
        if (tls is not null) settings["Tls"] = tls;
        Should.Throw<Exception>(() => Register(settings));
    }

    [Fact]
    public void Certificate_settings_are_not_silently_ignored_on_plain_connections()
    {
        var settings = Settings("mqtt://localhost");
        settings["ClientCertificate"] = new { Path = "unused.pfx" };
        Should.Throw<Exception>(() => Register(settings)).ToString().ShouldContain("certificates require");
    }

    [Fact]
    public async Task Certificate_loading_is_deferred_and_invalid_client_certificates_fail_build()
    {
        using var certificates = new Certificates();
        var settings = Settings("mqtts://localhost");
        settings["ClientCertificate"] = new { Path = "missing.pfx" };
        await using var missing = Register(settings, certificates.Directory).BuildServiceProvider();
        await Should.ThrowAsync<Exception>(async () => await missing.BuildApplicationAsync());
        settings["ClientCertificate"] = new { Path = "client.pfx", Password = "incorrect" };
        await using var badPassword = Register(settings, certificates.Directory).BuildServiceProvider();
        await Should.ThrowAsync<Exception>(async () => await badPassword.BuildApplicationAsync());
        settings["ClientCertificate"] = new { Path = "public-only.pfx" };
        await using var noKey = Register(settings, certificates.Directory).BuildServiceProvider();
        var error = await Should.ThrowAsync<Exception>(async () => await noKey.BuildApplicationAsync());
        error.ToString().ShouldContain("private key");
    }

    private static Dictionary<string, object> Settings(string address) => new()
    {
        ["Type"] = "mqtt.client", ["Address"] = address, ["ClientId"] = Guid.NewGuid().ToString("N"),
        ["Username"] = "test-user", ["Password"] = "test-password", ["AutoConnect"] = false,
        ["CertificateRevocationMode"] = "NoCheck", ["Timeout"] = "00:00:03"
    };

    private static IServiceCollection Register(Dictionary<string, object> settings, string? directory = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            Resources = new { broker = settings },
            Workflows = new { demo = new
            {
                connection = new { Type = "mqtt.connection", Connection = "broker" },
                subscriber = new { Type = "mqtt.subscriber", Connection = "broker", Topics = "test/#" },
                publisher = new { Type = "mqtt.publisher", Connection = "broker" }
            } }
        });
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddMqtt(directory);
        services.AddFluxFlowApplication(config);
        return services;
    }

    private static FlowMessage Command(string command) => FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { command })));
    private static BufferBlock<FlowMessage> Capture(ISourceBlock<FlowMessage> source)
    {
        var target = new BufferBlock<FlowMessage>();
        source.LinkTo(target);
        return target;
    }

    private static string ServerAddress(WebApplication server, string scheme)
        => new UriBuilder(server.Urls.Single()) { Scheme = scheme, Path = scheme is "ws" or "wss" ? "/mqtt" : "",
            Query = scheme is "ws" or "wss" ? "tenant=test" : "" }.Uri.AbsoluteUri;

    private static async Task<WebApplication> StartServer(string scheme, Certificates certificates)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen =>
        {
            if (scheme is "mqtts" or "wss") listen.UseHttps(https =>
            {
                https.ServerCertificate = certificates.Server;
                https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                https.ClientCertificateValidation = (certificate, _, _) => certificate.Thumbprint == certificates.Client.Thumbprint;
            });
            if (scheme is "mqtt" or "mqtts") listen.UseMqtt();
        }));
        builder.Services.AddHostedMqttServer(options => options.WithoutDefaultEndpoint());
        builder.Services.AddMqttConnectionHandler();
        builder.Services.AddConnections();
        var app = builder.Build();
        app.MapConnectionHandler<MqttConnectionHandler>("/mqtt", options =>
            options.WebSockets.SubProtocolSelector = protocols => protocols.Contains("mqtt") ? "mqtt" : "");
        app.UseMqttServer(server => server.ValidatingConnectionAsync += args =>
        {
            if (args.UserName != "test-user" || args.Password != "test-password") args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        });
        try { await app.StartAsync(); return app; }
        catch { await app.DisposeAsync(); throw; }
    }

    private sealed class Certificates : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "fluxflow-mqtt-certificates-" + Guid.NewGuid().ToString("N"));
        public X509Certificate2 Server { get; }
        public X509Certificate2 Client { get; }

        public Certificates(bool wrongName = false, bool expired = false)
        {
            System.IO.Directory.CreateDirectory(Directory);
            using var rootKey = RSA.Create(2048);
            var request = new CertificateRequest("CN=Test MQTT CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            using var root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(10));
            Server = Issue(root, server: true, wrongName, expired);
            Client = Issue(root, server: false);
            File.WriteAllText(Path.Combine(Directory, "ca.pem"), root.ExportCertificatePem());
            File.WriteAllBytes(Path.Combine(Directory, "client.pfx"), Client.Export(X509ContentType.Pkcs12, "pfx-password"));
            File.WriteAllText(Path.Combine(Directory, "client.pem"), Client.ExportCertificatePem());
            using var key = Client.GetRSAPrivateKey()!;
            File.WriteAllText(Path.Combine(Directory, "client.key"), key.ExportEncryptedPkcs8PrivateKeyPem("key-password",
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10000)));
#if NET9_0_OR_GREATER
            using var publicOnly = X509CertificateLoader.LoadCertificate(Client.Export(X509ContentType.Cert));
#else
            using var publicOnly = new X509Certificate2(Client.Export(X509ContentType.Cert));
#endif
            File.WriteAllBytes(Path.Combine(Directory, "public-only.pfx"), publicOnly.Export(X509ContentType.Pkcs12));
        }

        private static X509Certificate2 Issue(X509Certificate2 root, bool server, bool wrongName = false, bool expired = false)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest(server ? "CN=MQTT Server" : "CN=MQTT Client", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
            if (server)
            {
                var names = new SubjectAlternativeNameBuilder();
                if (wrongName) names.AddDnsName("wrong.example"); else names.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(names.Build());
            }
            using var certificate = request.Create(root, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 1), RandomNumberGenerator.GetBytes(16));
            using var withKey = certificate.CopyWithPrivateKey(key);
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(withKey.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.Exportable);
#endif
        }

        public void Dispose()
        {
            Server.Dispose();
            Client.Dispose();
            foreach (var file in System.IO.Directory.GetFiles(Directory)) File.Delete(file);
            System.IO.Directory.Delete(Directory);
        }
    }
}
