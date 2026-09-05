using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using Pulse.Mqtt.Transport;
using Pulse.Mqtt.Testing;
using Shouldly;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace FluxFlow.Components.Mqtt.PulseMqtt.Tests;

public sealed class PulseMqttTransportSessionTests
{
    [Theory]
    [InlineData(MqttBrokerTransport.Tcp, false, "tcp")]
    [InlineData(MqttBrokerTransport.Tcp, true, "tls")]
    [InlineData(MqttBrokerTransport.WebSocket, false, "ws")]
    [InlineData(MqttBrokerTransport.WebSocket, true, "wss")]
    public void Session_maps_all_four_portable_modes_to_exact_pulse_transport_options(
        MqttBrokerTransport transport,
        bool useTls,
        string expectedScheme)
    {
        using var certificate = CreateCertificate();
        var certificates = new X509Certificate2Collection(certificate);
        var broker = new MqttBrokerConfiguration
        {
            Host = transport == MqttBrokerTransport.WebSocket
                ? "2001:db8::1"
                : "broker.internal",
            Port = 9443,
            Transport = transport,
            UseTls = useTls,
            ServerName = transport == MqttBrokerTransport.Tcp
                ? "sni.internal"
                : null,
            WebSocketPath = transport == MqttBrokerTransport.WebSocket
                ? "/tenant mqtt"
                : "/mqtt"
        };

        var factory = PulseMqttTransportSession.CreateTransportFactory(broker, certificates);

        if (transport == MqttBrokerTransport.Tcp)
        {
            factory.ShouldBeOfType<TcpTransportFactory>();
            var options = PulseMqttTransportSession.CreateTcpOptions(broker, certificates);
            options.Host.ShouldBe("broker.internal");
            options.Port.ShouldBe(9443);
            options.UseTls.ShouldBe(useTls);
            options.TlsTargetHost.ShouldBe("sni.internal");
            options.ClientCertificates.ShouldBeSameAs(certificates);
            return;
        }

        factory.ShouldBeOfType<WebSocketTransportFactory>();
        var webSocket = PulseMqttTransportSession.CreateWebSocketOptions(
            broker,
            certificates);
        webSocket.Uri.Scheme.ShouldBe(expectedScheme);
        webSocket.Uri.AbsoluteUri.ShouldBe(
            $"{expectedScheme}://[2001:db8::1]:9443/tenant%20mqtt");
        webSocket.SubProtocol.ShouldBe("mqtt");
        var configure = webSocket.ConfigureClient.ShouldNotBeNull();
        using var client = new ClientWebSocket();
        configure(client.Options);
        client.Options.ClientCertificates
            .Cast<X509Certificate2>()
            .ShouldContain(certificate);
    }

    [Fact]
    public async Task SessionMapsExactContentAndSupportsReconnectWithoutOwningPolicy()
    {
        await using var broker = new PulseMqttTestBroker();
        var factory = new PulseMqttTransportFactory(broker);
        await using var session = await factory.CreateAsync(Configuration());

        await session.ConnectAsync();
        await session.SubscribeAsync("name:events", new MqttSubscriptionDefinition
        {
            TopicFilter = "events/+",
            Qos = MqttQos.AtLeastOnce,
            NoLocal = false,
            RetainAsPublished = true,
            RetainHandling = MqttRetainHandling.SendOnNewSubscription
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var next = ReadNextAsync(session.Messages, timeout.Token);

        await session.PublishAsync(new MqttPublishMessage
        {
            Topic = "events/one",
            Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/octet-stream"),
            Qos = MqttQos.AtLeastOnce,
            CorrelationData = "correlation-1",
            ResponseTopic = "responses/one",
            UserProperties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tenant"] = "north"
            }
        }, timeout.Token);
        var received = await next;

        received.Message.Topic.ShouldBe("events/one");
        received.Message.Content.Bytes.ToArray().ShouldBe([1, 2, 3]);
        received.Message.Content.ContentType.ShouldBe("application/octet-stream");
        received.Message.Qos.ShouldBe(MqttQos.AtLeastOnce);
        received.Message.CorrelationData.ShouldBe("correlation-1");
        received.Message.ResponseTopic.ShouldBe("responses/one");
        received.Message.UserProperties["tenant"].ShouldBe("north");
        received.Delivery.IsEmpty.ShouldBeFalse();

        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Ack, timeout.Token);
        await session.AcknowledgeAsync(received.Delivery, MqttWorkflowOutcome.Nak, timeout.Token);
        await session.UnsubscribeAsync("name:events", timeout.Token);
        await session.DisconnectAsync(cancellationToken: timeout.Token);
        session.IsConnected.ShouldBeFalse();

        await session.ConnectAsync(timeout.Token);
        session.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task FactoryHonorsCancellationBeforeCreatingSession()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new PulseMqttTransportFactory();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await factory.CreateAsync(Configuration(), cancellation.Token));
    }

    private static MqttClientConfiguration Configuration()
        => new()
        {
            Name = "client-1",
            ClientId = "client-1",
            Broker = new MqttBrokerConfiguration { Host = "broker.internal" },
            AutoConnect = MqttAutoConnectMode.Disabled
        };

    private static async Task<T> ReadNextAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        await foreach (var value in source.WithCancellation(cancellationToken))
            return value;
        throw new InvalidOperationException("The MQTT transport stream completed without a value.");
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=FluxFlow MQTT transport test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }
}
